using Microsoft.Extensions.Options;

namespace TaskTracker.Services;

/// <summary>Where and how task attachments are persisted on disk.</summary>
public sealed class AttachmentStorageOptions
{
    /// <summary>Secure root, outside the web root, where new attachments are written.</summary>
    public string RootPath { get; set; } = "";
    /// <summary>Legacy <c>wwwroot/uploads/attachments</c> root, read-only fallback for old files.</summary>
    public string? LegacyWebRootPath { get; set; }
}

/// <summary>
/// Stores attachment blobs outside the executable web root so they can never be served directly by
/// the static-file pipeline — every read must pass through the authorized download action. Existing
/// files under the legacy web-root location are still resolvable (read/delete) for backward
/// compatibility, but nothing new is ever written there.
/// </summary>
public interface IAttachmentStorage
{
    Task SaveAsync(int taskId, string storedName, Stream content, CancellationToken ct = default);
    /// <summary>Absolute path to the stored file (secure root, then legacy), or null if absent.</summary>
    string? ResolveExistingPath(int taskId, string storedName);
    /// <summary>Removes the file from every known location; true if anything was deleted.</summary>
    bool Delete(int taskId, string storedName);
}

public sealed class AttachmentStorage(IOptions<AttachmentStorageOptions> options) : IAttachmentStorage
{
    private readonly AttachmentStorageOptions _options = options.Value;

    public async Task SaveAsync(int taskId, string storedName, Stream content, CancellationToken ct = default)
    {
        var safeName = SafeName(storedName);
        var dir = Path.Combine(_options.RootPath, taskId.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, safeName);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    public string? ResolveExistingPath(int taskId, string storedName)
    {
        var safeName = SafeName(storedName);
        foreach (var root in Roots())
        {
            var path = Path.Combine(root, taskId.ToString(), safeName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public bool Delete(int taskId, string storedName)
    {
        var safeName = SafeName(storedName);
        var deleted = false;
        foreach (var root in Roots())
        {
            var path = Path.Combine(root, taskId.ToString(), safeName);
            if (File.Exists(path)) { File.Delete(path); deleted = true; }
        }
        return deleted;
    }

    private IEnumerable<string> Roots()
    {
        if (!string.IsNullOrEmpty(_options.RootPath)) yield return _options.RootPath;
        if (!string.IsNullOrEmpty(_options.LegacyWebRootPath)) yield return _options.LegacyWebRootPath!;
    }

    // Stored names are server-generated GUIDs, but never trust a name that could escape its folder.
    private static string SafeName(string storedName)
    {
        var name = Path.GetFileName(storedName);
        if (string.IsNullOrEmpty(name) || name != storedName)
            throw new ArgumentException("Invalid stored file name.", nameof(storedName));
        return name;
    }
}
