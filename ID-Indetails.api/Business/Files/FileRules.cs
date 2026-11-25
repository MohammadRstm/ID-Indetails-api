using System.Text;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Files;

/// <summary>
/// <list type="bullet">
/// <item>Only photos (JPEG, PNG, WebP, HEIC, up to 10 MB) and videos (MP4, MOV, WebM, up to 100 MB) are accepted.</item>
/// <item>What a file is comes from its first bytes, never from its name or the type the browser claims.</item>
/// <item>Voice notes (WebM, Ogg or MP4 audio, up to 10 MB) come in through their own route: WebM and MP4 audio look
/// like video in their first bytes, so the caller says it is a voice note.</item>
/// <item>Other files come in through their own route, for work entries and chats (25 November 2026): documents (PDF,
/// Office, CSV, text), ZIP, CAD drawings and 3D models, up to 50 MB. What they are comes from their extension, checked
/// against their first bytes; programs, web pages and scripts are never accepted.</item>
/// <item>The stored name is only for display: path parts and unusual characters are dropped.</item>
/// </list>
/// </summary>
public static class FileRules
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxVideoBytes = 100 * 1024 * 1024;
    public const long MaxAudioBytes = 10 * 1024 * 1024;
    public const long MaxDocumentBytes = 50 * 1024 * 1024;

    /// <summary>How many leading bytes <see cref="DetectDocument"/> reads: some files show what they are a little way in.</summary>
    public const int DocumentHeaderLength = PortalContentRules.HeaderLength;

    /// <summary>What one person may upload in a day, so nobody can fill the disk the database shares.</summary>
    public const long DailyBytesPerPerson = 2L * 1024 * 1024 * 1024;

    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    public const int MaxNameLength = 100;

    public sealed record Detected(FileKind Kind, string MimeType, string Extension);

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>The documents accepted, by extension: their type and how their first bytes look.</summary>
    private static readonly Dictionary<string, (string MimeType, Func<byte[], bool> Matches)> Documents = new()
    {
        [".pdf"] = ("application/pdf", b => Ascii(b.AsSpan(0, Math.Min(5, b.Length))) == "%PDF-"),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", b => b.AsSpan().StartsWith(Zip)),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", b => b.AsSpan().StartsWith(Zip)),
        [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", b => b.AsSpan().StartsWith(Zip)),
        [".doc"] = ("application/msword", b => b.AsSpan().StartsWith(Ole)),
        [".xls"] = ("application/vnd.ms-excel", b => b.AsSpan().StartsWith(Ole)),
        [".ppt"] = ("application/vnd.ms-powerpoint", b => b.AsSpan().StartsWith(Ole)),
        // plain text: no control characters but tabs and line breaks
        [".csv"] = ("text/csv", IsPlainText),
        [".txt"] = ("text/plain", IsPlainText),
        [".zip"] = ("application/zip", b => b.AsSpan().StartsWith(Zip)),
    };

    /// <summary>The extensions a document may have, for the error message and the client's picker.</summary>
    public static IReadOnlyCollection<string> DocumentExtensions => Documents.Keys;

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

    /// <summary>What a voice note really is, from its first <see cref="HeaderLength"/> bytes: WebM, Ogg or MP4 audio; null for anything else.</summary>
    public static Detected? DetectVoice(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
        {
            return null;
        }

        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return new(FileKind.Audio, "audio/webm", ".webm");
        }

        if (Ascii(header[..4]) == "OggS")
        {
            return new(FileKind.Audio, "audio/ogg", ".ogg");
        }

        var brand = Ascii(header[8..12]);
        return Ascii(header[4..8]) == "ftyp" && (brand == "M4A " || Mp4Brands.Contains(brand))
            ? new(FileKind.Audio, "audio/mp4", ".m4a")
            : null;
    }

    /// <summary>
    /// What a document really is: the type its extension stands for, when its first bytes (up to
    /// <see cref="HeaderLength"/>) fit it; null for anything else.
    /// </summary>
    public static Detected? DetectDocument(string? fileName, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var bytes = header.ToArray();
        if (Documents.TryGetValue(extension, out var type))
        {
            return type.Matches(bytes) ? new(FileKind.Document, type.MimeType, extension) : null;
        }

        // the drawings and 3D models a project's files take, checked the same way; photos and videos have their own route
        var other = PortalContentRules.TypeOf(extension);
        if (other is null || other.Preview is PortalPreview.Image or PortalPreview.Video
            || other.ContentType.StartsWith("image/", StringComparison.Ordinal) && other.Extension is not ("dwg" or "dxf")
            || other.ContentType.StartsWith("video/", StringComparison.Ordinal) || !PortalContentRules.MatchesContent(other, bytes))
        {
            return null;
        }

        // stored as a file, never as a picture: a drawing's "image/vnd.dwg" would be taken for a photo
        return new(FileKind.Document, other.ContentType.StartsWith("image/", StringComparison.Ordinal) ? "application/octet-stream" : other.ContentType, extension);
    }

    private static bool IsPlainText(byte[] b) => b.Length > 0 && b.All(c => c >= 0x20 || c is 0x09 or 0x0A or 0x0D);

    /// <summary>A stored file's kind, from the type found when it was uploaded.</summary>
    public static FileKind KindOf(string mimeType)
        => mimeType.StartsWith("image/", StringComparison.Ordinal) ? FileKind.Image
            : mimeType.StartsWith("audio/", StringComparison.Ordinal) ? FileKind.Audio
            : mimeType.StartsWith("video/", StringComparison.Ordinal) ? FileKind.Video
            : FileKind.Document;

    public static RuleDenial? CheckSize(FileKind kind, long sizeBytes)
    {
        var (max, label) = kind switch
        {
            FileKind.Image => (MaxImageBytes, "Photos"),
            FileKind.Audio => (MaxAudioBytes, "Voice notes"),
            FileKind.Document => (MaxDocumentBytes, "Files"),
            _ => (MaxVideoBytes, "Videos"),
        };
        return sizeBytes > max ? RuleDenial.BusinessRule(ErrorCodes.FileTooLarge, $"{label} can be at most {max / 1024 / 1024} MB.") : null;
    }

    /// <param name="uploadedToday">What the person uploaded in the last 24 hours, in bytes.</param>
    public static RuleDenial? CheckDailyQuota(long uploadedToday, long sizeBytes)
        => uploadedToday + sizeBytes > DailyBytesPerPerson
            ? RuleDenial.BusinessRule(ErrorCodes.UploadQuotaReached, $"You can upload up to {DailyBytesPerPerson / 1024 / 1024 / 1024} GB a day. Try again tomorrow.")
            : null;

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
