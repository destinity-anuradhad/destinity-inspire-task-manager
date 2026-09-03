namespace TaskTracker.Services;

/// <summary>
/// Validates that an uploaded file's actual content matches its claimed extension by inspecting the
/// leading "magic" bytes. This defeats the common bypass of renaming a dangerous file (e.g. an
/// executable or script) to an allowed extension — the extension allow-list alone cannot catch that.
/// Plain-text formats have no reliable signature and are accepted by extension.
/// </summary>
public static class FileSignatureValidator
{
    // Text formats carry no binary signature; they're low-risk and always served as downloads.
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".md" };

    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = [[0x25, 0x50, 0x44, 0x46]],                                   // %PDF
        [".png"]  = [[0x89, 0x50, 0x4E, 0x47]],
        [".jpg"]  = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"]  = [[0x47, 0x49, 0x46, 0x38]],                                   // GIF8
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],                                   // RIFF (....WEBP)
        [".zip"]  = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08]],
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],                                   // OOXML is a zip
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".doc"]  = [[0xD0, 0xCF, 0x11, 0xE0]],                                   // OLE2 compound
        [".xls"]  = [[0xD0, 0xCF, 0x11, 0xE0]],
    };

    /// <summary>Whether the extension is one the system accepts at all.</summary>
    public static bool IsAllowedExtension(string? extension) =>
        extension != null && (TextExtensions.Contains(extension) || Signatures.ContainsKey(extension));

    /// <summary>Number of leading bytes worth reading from a file to validate it.</summary>
    public const int HeaderLength = 8;

    /// <summary>
    /// True when <paramref name="header"/> matches a known signature for <paramref name="extension"/>.
    /// Text extensions always pass; unknown extensions always fail.
    /// </summary>
    public static bool ContentMatches(string? extension, ReadOnlySpan<byte> header)
    {
        if (extension == null) return false;
        if (TextExtensions.Contains(extension)) return true;
        if (!Signatures.TryGetValue(extension, out var candidates)) return false;

        foreach (var signature in candidates)
        {
            if (header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature))
                return true;
        }
        return false;
    }
}
