namespace TaskTracker.Services;

/// <summary>
/// Issues, lists, revokes, and authenticates API keys backed by <see cref="TaskTracker.Models.ApiClient"/>.
/// Secrets are never stored; only a SHA-256 hash of the secret portion is persisted.
/// </summary>
public interface IApiClientService
{
    /// <summary>
    /// Lists API clients. When <paramref name="includeGlobal"/> is <c>true</c> (system administrators)
    /// every client is returned; otherwise only clients scoped to <paramref name="teamId"/> are shown.
    /// </summary>
    Task<IReadOnlyList<ApiClientSummary>> ListAsync(int? teamId, bool includeGlobal, CancellationToken ct = default);

    /// <summary>
    /// Creates a new API client and returns the one-time plaintext key. The plaintext is never
    /// recoverable afterwards.
    /// </summary>
    Task<ApiClientCreateResult> CreateAsync(ApiClientCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Revokes a client. Team administrators may only revoke keys scoped to <paramref name="teamId"/>;
    /// system administrators (<paramref name="isSysAdmin"/>) may revoke any key. Returns <c>false</c>
    /// when the key does not exist or the caller is not permitted to revoke it.
    /// </summary>
    Task<bool> RevokeAsync(int id, int? teamId, bool isSysAdmin, CancellationToken ct = default);

    /// <summary>
    /// Validates a presented key and resolves the acting principal (owner user + scoped team + team role
    /// + scopes). Returns <c>null</c> for any invalid, expired, revoked, or no-longer-authorized key.
    /// Best-effort updates <c>LastUsedAt</c>/<c>LastUsedIp</c>.
    /// </summary>
    Task<ApiClientPrincipal?> AuthenticateAsync(string presentedKey, string? ip, CancellationToken ct = default);

    /// <summary>The keys owned by <paramref name="ownerUserId"/> (any team), newest first.</summary>
    Task<IReadOnlyList<ApiClientSummary>> ListForOwnerAsync(int ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// Mints a personal key for any signed-in user: owned by them, scoped to their active team, with an
    /// expiry of at most <see cref="PersonalKeyRules.MaxDays"/> days and at most
    /// <see cref="PersonalKeyRules.MaxActiveKeys"/> active keys per person. Throws
    /// <see cref="ArgumentException"/> when a rule is broken.
    /// </summary>
    Task<ApiClientCreateResult> CreatePersonalAsync(PersonalKeyRequest request, CancellationToken ct = default);

    /// <summary>Revokes one of the caller's own keys. Returns <c>false</c> if it is not theirs.</summary>
    Task<bool> RevokeOwnAsync(int id, int ownerUserId, CancellationToken ct = default);
}

/// <summary>Limits on self-service personal keys.</summary>
public static class PersonalKeyRules
{
    public const int MaxDays = 365;
    public const int MaxActiveKeys = 10;
}

/// <summary>Request to mint a personal key (see <see cref="IApiClientService.CreatePersonalAsync"/>).</summary>
public sealed record PersonalKeyRequest(
    string Name,
    int OwnerUserId,
    bool OwnerIsAdmin,
    int? TeamId,
    IEnumerable<string> Scopes,
    int ExpiresInDays,
    bool OwnTasksOnly);

/// <summary>Request to mint a new API client.</summary>
public sealed record ApiClientCreateRequest(
    string Name,
    int OwnerUserId,
    int? TeamId,
    IEnumerable<string> Scopes,
    DateTime? ExpiresAt,
    bool OwnTasksOnly = false);

/// <summary>Result of minting a key: the stored summary plus the one-time plaintext secret.</summary>
public sealed record ApiClientCreateResult(ApiClientSummary Client, string PlaintextKey);

/// <summary>Display-safe projection of a stored API client (never includes the secret).</summary>
public sealed record ApiClientSummary(
    int Id,
    string Name,
    string KeyPrefix,
    string Scopes,
    int OwnerUserId,
    string OwnerDisplayName,
    int? TeamId,
    string? TeamName,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt,
    string? LastUsedIp,
    bool IsActive,
    bool OwnTasksOnly = false);

/// <summary>The resolved identity behind a valid API key, used to build the request principal.</summary>
public sealed record ApiClientPrincipal(
    int ClientId,
    string ClientName,
    int UserId,
    string Username,
    string DisplayName,
    string Role,
    int? TeamId,
    string TeamName,
    string TeamRole,
    IReadOnlyList<string> Scopes,
    bool OwnTasksOnly = false);
