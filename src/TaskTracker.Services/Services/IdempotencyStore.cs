using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

/// <summary>Persists and looks up cached API write responses keyed by (API client, Idempotency-Key).</summary>
public interface IIdempotencyStore
{
    Task<ApiIdempotencyRecord?> TryGetAsync(int apiClientId, string key, CancellationToken ct = default);
    Task SaveAsync(int apiClientId, string key, string method, string path, int statusCode, string? body, CancellationToken ct = default);
}

public sealed class IdempotencyStore(AppDbContext db) : IIdempotencyStore
{
    public Task<ApiIdempotencyRecord?> TryGetAsync(int apiClientId, string key, CancellationToken ct = default)
        => db.ApiIdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ApiClientId == apiClientId && r.IdempotencyKey == key, ct);

    public async Task SaveAsync(int apiClientId, string key, string method, string path, int statusCode, string? body, CancellationToken ct = default)
    {
        db.ApiIdempotencyRecords.Add(new ApiIdempotencyRecord
        {
            ApiClientId = apiClientId,
            IdempotencyKey = key,
            Method = method,
            Path = path.Length > 500 ? path[..500] : path,
            StatusCode = statusCode,
            ResponseBody = body,
            CreatedAt = DateTime.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key won the unique-index race; keep the context usable.
            db.ChangeTracker.Clear();
        }
    }
}
