using System.Text;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Files;

/// <summary>
/// <list type="bullet">
/// <item>Only photos (JPEG, PNG, WebP, HEIC, up to 10 MB) and videos (MP4, MOV, WebM, up to 100 MB) are accepted.</item>
/// <item>What a file is comes from its first bytes, never from its name or the type the browser claims.</item>
/// <item>The stored name is only for display: path parts and unusual characters are dropped.</item>
/// </list>
/// </summary>
public static class FileRules
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxVideoBytes = 100 * 1024 * 1024;

    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    public const int MaxNameLength = 100;

    public sealed record Detected(FileKind Kind, string MimeType, string Extension);

    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "mif1", "msf1"];
    private static readonly string[] Mp4Brands = ["isom", "iso2", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "M4V ", "dash", "MSNV"];

    /// <summary>What the file really is, from its first <see cref="HeaderLength"/> bytes; null when it isn't one we accept.</summary>
    public static Detected? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
        {
            return null;
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return new(FileKind.Image, "image/jpeg", ".jpg");
        }

        if (header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new(FileKind.Image, "image/png", ".png");
        }

        if (Ascii(header[..4]) == "RIFF" && Ascii(header[8..12]) == "WEBP")
        {
            return new(FileKind.Image, "image/webp", ".webp");
        }

        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return new(FileKind.Video, "video/webm", ".webm");
        }

        // ISO media files start with a box: 4 bytes of size, "ftyp", then the major brand.
        if (Ascii(header[4..8]) != "ftyp")
        {
            return null;
        }

        var brand = Ascii(header[8..12]);
        if (HeicBrands.Contains(brand))
        {
            return new(FileKind.Image, "image/heic", ".heic");
        }

        if (brand == "qt  ")
        {
            return new(FileKind.Video, "video/quicktime", ".mov");
        }

        return Mp4Brands.Contains(brand) ? new(FileKind.Video, "video/mp4", ".mp4") : null;
    }

    /// <summary>A stored file's kind, from the type found when it was uploaded.</summary>
    public static FileKind KindOf(string mimeType) => mimeType.StartsWith("image/", StringComparison.Ordinal) ? FileKind.Image : FileKind.Video;

    public static RuleDenial? CheckSize(FileKind kind, long sizeBytes)
    {
        var max = kind == FileKind.Image ? MaxImageBytes : MaxVideoBytes;
        return sizeBytes > max
            ? RuleDenial.BusinessRule(ErrorCodes.FileTooLarge, $"{(kind == FileKind.Image ? "Photos" : "Videos")} can be at most {max / 1024 / 1024} MB.")
            : null;
    }

    /// <summary>The client's file name made safe to show and to send back: no path, plain characters, the real extension.</summary>
    public static string SafeName(string? clientName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName((clientName ?? string.Empty).Replace('\\', '/')));
        var kept = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray()).Trim(' ', '.');
        if (kept.Length == 0)
        {
            kept = "file";
        }

        return (kept.Length > MaxNameLength ? kept[..MaxNameLength] : kept) + extension;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);
}
