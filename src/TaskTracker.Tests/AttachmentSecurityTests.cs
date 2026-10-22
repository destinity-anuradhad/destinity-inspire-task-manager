using Microsoft.Extensions.Options;
using TaskTracker.Services;
using Xunit;

namespace TaskTracker.Tests;

public class AttachmentSecurityTests
{
    // ── Signature validation ──────────────────────────────────────────────────

    [Fact]
    public void IsAllowedExtension_AcceptsKnownTypes_RejectsDangerous()
    {
        Assert.True(FileSignatureValidator.IsAllowedExtension(".png"));
        Assert.True(FileSignatureValidator.IsAllowedExtension(".pdf"));
        Assert.True(FileSignatureValidator.IsAllowedExtension(".txt"));
        Assert.False(FileSignatureValidator.IsAllowedExtension(".exe"));
        Assert.False(FileSignatureValidator.IsAllowedExtension(".js"));
        Assert.False(FileSignatureValidator.IsAllowedExtension(null));
    }

    [Fact]
    public void ContentMatches_AcceptsRealSignature_RejectsMismatch()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
        byte[] mzExe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];   // "MZ" PE header

        Assert.True(FileSignatureValidator.ContentMatches(".png", png));
        Assert.True(FileSignatureValidator.ContentMatches(".pdf", pdf));

        // An executable renamed to .png is rejected even though the extension is allowed.
        Assert.False(FileSignatureValidator.ContentMatches(".png", mzExe));
        // A PDF's bytes don't satisfy a .png claim.
        Assert.False(FileSignatureValidator.ContentMatches(".png", pdf));
    }

    [Fact]
    public void ContentMatches_TextTypes_AlwaysPass()
    {
        byte[] anything = [0x00, 0x01, 0x02, 0x03];
        Assert.True(FileSignatureValidator.ContentMatches(".txt", anything));
        Assert.True(FileSignatureValidator.ContentMatches(".csv", anything));
    }

    // ── Storage (secure root + legacy fallback) ───────────────────────────────

    [Fact]
    public async Task Storage_Save_Resolve_Delete_UsesSecureRoot()
    {
        var (root, legacy) = TempRoots();
        try
        {
            var storage = NewStorage(root, legacy);
            await storage.SaveAsync(42, "abc.png", new MemoryStream([1, 2, 3]));

            var path = storage.ResolveExistingPath(42, "abc.png");
            Assert.NotNull(path);
            Assert.StartsWith(root, path!);                 // written under the secure, non-web-root path
            Assert.True(File.Exists(path));

            Assert.True(storage.Delete(42, "abc.png"));
            Assert.Null(storage.ResolveExistingPath(42, "abc.png"));
        }
        finally { Cleanup(root, legacy); }
    }

    [Fact]
    public async Task Storage_ResolvesLegacyFile_WhenNotInSecureRoot()
    {
        var (root, legacy) = TempRoots();
        try
        {
            // Simulate a pre-existing attachment in the old web-root location.
            var legacyDir = Path.Combine(legacy, "7");
            Directory.CreateDirectory(legacyDir);
            await File.WriteAllBytesAsync(Path.Combine(legacyDir, "old.pdf"), [0x25, 0x50, 0x44, 0x46]);

            var storage = NewStorage(root, legacy);
            var path = storage.ResolveExistingPath(7, "old.pdf");

            Assert.NotNull(path);
            Assert.StartsWith(legacy, path!);
        }
        finally { Cleanup(root, legacy); }
    }

    [Fact]
    public void Storage_RejectsTraversalNames()
    {
        var (root, legacy) = TempRoots();
        try
        {
            var storage = NewStorage(root, legacy);
            Assert.Throws<ArgumentException>(() => storage.ResolveExistingPath(1, "../secret.txt"));
        }
        finally { Cleanup(root, legacy); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AttachmentStorage NewStorage(string root, string legacy) =>
        new(Options.Create(new AttachmentStorageOptions { RootPath = root, LegacyWebRootPath = legacy }));

    private static (string root, string legacy) TempRoots()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "tt-att-" + Guid.NewGuid().ToString("N"));
        return (Path.Combine(baseDir, "secure"), Path.Combine(baseDir, "legacy"));
    }

    private static void Cleanup(string root, string legacy)
    {
        var baseDir = Path.GetDirectoryName(root);
        if (baseDir != null && Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
    }
}
