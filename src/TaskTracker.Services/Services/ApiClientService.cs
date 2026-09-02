using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public sealed class ApiClientService(AppDbContext db) : IApiClientService
{
    // Public, human-recognisable label embedded in every key prefix (e.g. "tt_a1B2c3d4").
    private const string PrefixLabel = "tt";
    // How stale LastUsedAt must be before we pay for another write.
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ApiClientSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default)
    {
        var query = db.ApiClients.AsNoTracking();
        if (!includeGlobal)
        {
            if (teamId is null) return [];
            query = query.Where(c => c.TeamId == teamId);
        }

        var clients = await query
            .OrderByDescending(c => c.RevokedAt == null)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);

        var ownerIds = clients.Select(c => c.OwnerUserId).Distinct().ToList();
        var teamIds  = clients.Where(c => c.TeamId != null).Select(c => c.TeamId!.Value).Distinct().ToList();

        var owners = await db.AppUsers.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var teamNames = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var now = DateTime.UtcNow;
        return clients.Select(c => ToSummary(c, owners, teamNames, now)).ToList();
    }

    public async Task<ApiClientCreateResult> CreateAsync(ApiClientCreateRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
            throw new ArgumentException("An API client name is required.", nameof(request));

        var scopes = ApiScopes.Normalize(request.Scopes ?? []);
        if (scopes.Length == 0)
            throw new ArgumentException("At least one valid scope is required.", nameof(request));

        // prefix = public identifier (safe to display/log); secret = the actual credential.
        // The presented key is "{prefix}.{secret}"; only SHA-256(secret) is persisted.
        var prefix = PrefixLabel + "_" + RandomToken(6);   // 3 + 8 = 11 chars (<= 12)
        var secret = RandomToken(32);
        var fullKey = prefix + "." + secret;

        var client = new ApiClient
        {
            Name        = name,
            KeyPrefix   = prefix,
            KeyHash     = Hash(secret),
            Scopes      = scopes,
            OwnerUserId = request.OwnerUserId,
            TeamId      = request.TeamId,
            CreatedAt   = DateTime.UtcNow,
            ExpiresAt   = request.ExpiresAt,
        };
        db.ApiClients.Add(client);
        await db.SaveChangesAsync(ct);

        var owners = new Dictionary<int, string>();
        var owner = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == client.OwnerUserId, ct);
        if (owner != null) owners[owner.Id] = owner.DisplayName;

        var teamNames = new Dictionary<int, string>();
        if (client.TeamId is int tid)
        {
            var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct);
            if (team != null) teamNames[tid] = team.Name;
        }

        var summary = ToSummary(client, owners, teamNames, DateTime.UtcNow);
        return new ApiClientCreateResult(summary, fullKey);
    }

    public async Task<bool> RevokeAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default)
    {
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client == null) return false;
        if (!isSysAdmin && client.TeamId != teamId) return false;   // scoped: only revoke own team's keys
        if (client.RevokedAt != null) return true;                  // already revoked — idempotent

        client.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ApiClientPrincipal?> AuthenticateAsync(string presentedKey, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey)) return null;

        var dot = presentedKey.IndexOf('.');
        if (dot <= 0 || dot >= presentedKey.Length - 1) return null;
        var prefix = presentedKey[..dot];
        var secret = presentedKey[(dot + 1)..];

        var hash = Hash(secret);
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.KeyHash == hash, ct);
        if (client == null) return null;
        if (!FixedTimeEquals(client.KeyPrefix, prefix)) return null;   // prefix must also match the hashed secret

        var now = DateTime.UtcNow;
        if (client.RevokedAt != null) return null;
        if (client.ExpiresAt is { } expires && expires <= now) return null;

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == client.OwnerUserId, ct);
        if (user == null || !string.Equals(user.Status, "Active", StringComparison.Ordinal)) return null;

        int? teamId = client.TeamId;
        var teamName = "";
        var teamRole = "";
        if (teamId is int tid)
        {
            var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid && t.IsActive, ct);
            if (team == null) return null;
            teamName = team.Name;

            if (user.Role == "Admin")
            {
                teamRole = "TeamAdmin";
            }
            else
            {
                var membership = await db.TeamMembers.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.TeamId == tid && m.UserId == user.Id, ct);
                if (membership == null) return null;   // owner was removed from the team → key is inert
                teamRole = membership.TeamRole;
            }
        }
        else if (user.Role != "Admin")
        {
            return null;   // non-admin keys must be team-scoped to resolve an active team
        }

        await TouchLastUsedAsync(client, ip, now, ct);

        return new ApiClientPrincipal(
            client.Id, client.Name, user.Id, user.Username, user.DisplayName, user.Role,
            teamId, teamName, teamRole, ApiScopes.Parse(client.Scopes));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task TouchLastUsedAsync(ApiClient client, string? ip, DateTime now, CancellationToken ct)
    {
        if (client.LastUsedAt is { } last && now - last < LastUsedThrottle) return;

        try
        {
            client.LastUsedAt = now;
            if (!string.IsNullOrEmpty(ip))
                client.LastUsedIp = ip.Length > 45 ? ip[..45] : ip;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Usage bookkeeping is best-effort and must never block a valid request.
            db.ChangeTracker.Clear();
        }
    }

    private static ApiClientSummary ToSummary(
        ApiClient c, IReadOnlyDictionary<int, string> owners, IReadOnlyDictionary<int, string> teamNames, DateTime now)
    {
        var active = c.RevokedAt == null && (c.ExpiresAt == null || c.ExpiresAt > now);
        return new ApiClientSummary(
            c.Id, c.Name, c.KeyPrefix, c.Scopes,
            c.OwnerUserId, owners.GetValueOrDefault(c.OwnerUserId, $"User #{c.OwnerUserId}"),
            c.TeamId, c.TeamId is int tid ? teamNames.GetValueOrDefault(tid) : null,
            c.CreatedAt, c.ExpiresAt, c.RevokedAt, c.LastUsedAt, c.LastUsedIp, active);
    }

    private static string RandomToken(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
