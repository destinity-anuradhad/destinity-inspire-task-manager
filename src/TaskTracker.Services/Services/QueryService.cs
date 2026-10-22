using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class QueryService(AppDbContext db) : IQueryService
{
    private readonly AppDbContext _db = db;

    public async Task<List<SavedQuery>> GetQueriesAsync(string displayName, int? teamId)
        => await _db.SavedQueries
            .Where(q => (q.CreatedBy == displayName || q.IsShared) &&
                        (!teamId.HasValue || q.TeamId == null || q.TeamId == teamId))
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();

    public async Task<(bool Success, string? Error)> SaveQueryAsync(
        string name, string? description, bool isShared, string filters,
        string displayName, int? teamId)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "Name is required");
        try { JsonDocument.Parse(filters); } catch { filters = "{}"; }
        _db.SavedQueries.Add(new SavedQuery {
            TeamId      = teamId,
            Name        = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Filters     = filters,
            IsShared    = isShared,
            CreatedBy   = displayName,
            CreatedAt   = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, bool IsShared)> ToggleShareAsync(
        int id, string displayName, bool isAdmin, int? teamId)
    {
        var q = await _db.SavedQueries.FindAsync(id);
        if (q == null) return (false, "Not found", false);
        if (teamId.HasValue && q.TeamId.HasValue && q.TeamId != teamId) return (false, "Not found", false);
        if (q.CreatedBy != displayName && !isAdmin) return (false, "Unauthorized", false);
        q.IsShared = !q.IsShared;
        await _db.SaveChangesAsync();
        return (true, null, q.IsShared);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(
        int id, string displayName, bool isAdmin, int? teamId)
    {
        var q = await _db.SavedQueries.FindAsync(id);
        if (q == null) return (false, "Not found");
        if (teamId.HasValue && q.TeamId.HasValue && q.TeamId != teamId) return (false, "Not found");
        if (q.CreatedBy != displayName && !isAdmin) return (false, "You can only delete your own queries");
        _db.SavedQueries.Remove(q);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<Dictionary<string, string>?> GetFiltersAsync(int id, string? displayName = null, bool isAdmin = false, int? teamId = null)
    {
        var q = await _db.SavedQueries.FindAsync(id);
        if (q == null) return null;
        if (teamId.HasValue && q.TeamId.HasValue && q.TeamId != teamId) return null;
        if (!isAdmin && q.CreatedBy != displayName && !q.IsShared) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(q.Filters) ?? []; }
        catch { return []; }
    }
}
