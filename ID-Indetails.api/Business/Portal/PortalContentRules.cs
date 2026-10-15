using System.Globalization;
using System.Text;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>
/// <list type="bullet">
/// <item>The client portal takes the files a design studio hands its clients: images, videos, PDFs, Office files,
/// drawings and 3D models. Never web pages, SVG or programs. There is no size limit.</item>
/// <item>What a file is comes from its extension, checked against its first bytes once it has arrived; the type the
/// browser claims is never used.</item>
/// <item>Files go to the store in parts of one size (the last may be smaller), at least 16 MiB and few enough for the
/// store's 10,000 parts.</item>
/// <item>Content can change on any Design client project that isn't archived.</item>
/// </list>
/// </summary>
public static class PortalContentRules
{
    public const int MinPartSize = 16 * 1024 * 1024;
    public const int MaxParts = 10_000;

    /// <summary>How many leading bytes <see cref="MatchesContent"/> reads.</summary>
    public const int HeaderLength = 4096;

    public const int MaxTitleLength = 200;
    public const int MaxNameLength = 255;

    /// <summary>The gap left between neighbours in a section's order.</summary>
    public const int SortStep = 1024;

    /// <summary>How a file of one extension is stored and shown, and how its first bytes are checked.</summary>
    public sealed record FileType(string Extension, string ContentType, PortalPreview Preview, Func<byte[], bool> Matches);

    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Ebml = [0x1A, 0x45, 0xDF, 0xA3];
    private static readonly string[] MovAtoms = ["ftyp", "moov", "mdat", "wide", "free", "skip", "pnot"];

    private static readonly FileType[] Types =
    [
        // images
        new("jpg", "image/jpeg", PortalPreview.Image, b => StartsWith(b, [0xFF, 0xD8, 0xFF])),
        new("jpeg", "image/jpeg", PortalPreview.Image, b => StartsWith(b, [0xFF, 0xD8, 0xFF])),
        new("png", "image/png", PortalPreview.Image, b => StartsWith(b, Png)),
        new("webp", "image/webp", PortalPreview.Image, b => Ascii(b, 0, 4) == "RIFF" && Ascii(b, 8, 4) == "WEBP"),
        new("gif", "image/gif", PortalPreview.Image, b => Ascii(b, 0, 6) is "GIF87a" or "GIF89a"),
        new("heic", "image/heic", PortalPreview.None, IsoMedia),
        new("heif", "image/heif", PortalPreview.None, IsoMedia),
        new("tif", "image/tiff", PortalPreview.None, Tiff),
        new("tiff", "image/tiff", PortalPreview.None, Tiff),

        // video
        new("mp4", "video/mp4", PortalPreview.Video, IsoMedia),
        new("m4v", "video/mp4", PortalPreview.Video, IsoMedia),
        new("mov", "video/quicktime", PortalPreview.None, b => MovAtoms.Contains(Ascii(b, 4, 4))),
        new("webm", "video/webm", PortalPreview.Video, b => StartsWith(b, Ebml)),

        // documents
        new("pdf", "application/pdf", PortalPreview.Pdf, b => Ascii(b, 0, Math.Min(b.Length, 1024)).Contains("%PDF-", StringComparison.Ordinal)),
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", PortalPreview.None, Zip),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", PortalPreview.None, Zip),
        new("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", PortalPreview.None, Zip),
        new("zip", "application/zip", PortalPreview.None, Zip),

        // drawings
        new("dwg", "image/vnd.dwg", PortalPreview.None, b => Ascii(b, 0, 4) == "AC10" || (Ascii(b, 0, 3) == "AC1" && char.IsAsciiDigit((char)At(b, 3)))),
        new("dxf", "image/vnd.dxf", PortalPreview.None, b => Ascii(b, 0, 18) == "AutoCAD Binary DXF" || Text(b)),
        new("rvt", "application/octet-stream", PortalPreview.None, b => StartsWith(b, Ole)),
        new("ifc", "application/x-step", PortalPreview.None, b => Text(b) && Ascii(b, 0, Math.Min(b.Length, 64)).Contains("ISO-10303-21", StringComparison.Ordinal)),
        new("skp", "application/vnd.sketchup.skp", PortalPreview.None, b => StartsWith(b, [0xFF, 0xFE, 0xFF])),
        new("3dm", "application/octet-stream", PortalPreview.None, b => Ascii(b, 0, 23) == "3D Geometry File Format"),

        // 3D models
        new("glb", "model/gltf-binary", PortalPreview.None, b => Ascii(b, 0, 4) == "glTF"),
        new("gltf", "model/gltf+json", PortalPreview.None, Text),
        new("obj", "model/obj", PortalPreview.None, Text),
        new("fbx", "application/octet-stream", PortalPreview.None, b => Ascii(b, 0, 18) == "Kaydara FBX Binary" || Text(b)),
        // binary STL has no signature: anything that isn't plainly something else
        new("stl", "model/stl", PortalPreview.None, _ => true),
        new("3ds", "application/octet-stream", PortalPreview.None, b => StartsWith(b, [0x4D, 0x4D])),
        new("max", "application/octet-stream", PortalPreview.None, b => StartsWith(b, Ole)),
        new("blend", "application/octet-stream", PortalPreview.None, b => Ascii(b, 0, 7) == "BLENDER"),
        new("usdz", "model/vnd.usdz+zip", PortalPreview.None, Zip),
        new("dae", "model/vnd.collada+xml", PortalPreview.None, Text),
    ];

    private static readonly Dictionary<string, FileType> ByExtension = Types.ToDictionary(t => t.Extension);

    /// <summary>The accepted extensions, lower case and without the dot, in a stable order.</summary>
    public static IReadOnlyList<string> AcceptedExtensions { get; } = Types.Select(t => t.Extension).ToList();

    /// <summary>The type a file name's extension stands for; null when the client portal doesn't take it.</summary>
    public static FileType? TypeOf(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ByExtension.GetValueOrDefault(extension);
    }

    /// <summary>The type for a stored content type, for showing a file; the first extension that uses it.</summary>
    public static PortalPreview PreviewOf(string contentType)
        => Types.FirstOrDefault(t => t.ContentType == contentType)?.Preview ?? PortalPreview.None;

    /// <summary>Whether the first bytes fit the type. Programs and web pages are refused whatever the extension says.</summary>
    public static bool MatchesContent(FileType type, byte[] header)
        => header.Length > 0 && !IsDangerous(header) && type.Matches(header);

    /// <summary>The part size for a file: one size for every part but the last, at least 16 MiB, at most 10,000 parts.</summary>
    public static long PartSizeFor(long sizeBytes)
    {
        const long mib = 1024 * 1024;
        var needed = (sizeBytes + MaxParts - 1) / MaxParts;
        var rounded = (needed + mib - 1) / mib * mib;
        return Math.Max(MinPartSize, rounded);
    }

    public static int PartCountFor(long sizeBytes, long partSize) => (int)((sizeBytes + partSize - 1) / partSize);

    /// <summary>
    /// The part numbers still missing or of the wrong size. Every part but the last is <paramref name="partSize"/>
    /// bytes; the last holds the rest. Empty when the upload is whole.
    /// </summary>
    public static IReadOnlyList<int> PartsNotReceived(IEnumerable<(int Number, long Size)> received, long sizeBytes, long partSize)
    {
        var count = PartCountFor(sizeBytes, partSize);
        var sizes = received.GroupBy(p => p.Number).ToDictionary(g => g.Key, g => g.First().Size);
        var missing = new List<int>();
        for (var number = 1; number <= count; number++)
        {
            var expected = number < count ? partSize : sizeBytes - (partSize * (count - 1));
            if (!sizes.TryGetValue(number, out var size) || size != expected)
            {
                missing.Add(number);
            }
        }

        return missing;
    }

    /// <summary>A new, unguessable key under the project's folder. Never holds the file name.</summary>
    public static string NewStorageKey(long projectId) => $"portal/{projectId}/{Guid.NewGuid():N}";

    /// <summary>The prefix every client portal key starts with.</summary>
    public const string KeyPrefix = "portal/";

    /// <summary>
    /// The browser's file name made safe to keep and show: no path, no control or invisible formatting characters (such
    /// as a right-to-left override that makes "gpj.exe" read "exe.jpg"), at most 255 characters.
    /// </summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var kept = new string(name
            .Where(c => char.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format)
                && c is not ('"' or '<' or '>' or '|' or '*' or '?' or ':'))
            .ToArray()).Trim();
        if (kept.Length <= MaxNameLength)
        {
            return kept;
        }

        // the extension is kept, unless it is too long to be a real one
        var extension = Path.GetExtension(kept);
        return extension.Length < MaxNameLength
            ? kept[..(MaxNameLength - extension.Length)] + extension
            : kept[..MaxNameLength];
    }

    /// <summary>A new item's title: the file's name without its extension, shortened to fit.</summary>
    public static string DefaultTitle(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (title.Length == 0)
        {
            title = fileName;
        }

        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    /// <summary>The place after the last item of a section.</summary>
    public static int NextSortOrder(int? lastSortOrder) => (lastSortOrder ?? 0) + SortStep;

    /// <summary>Whether a project has a client portal at all: a Design project for a client.</summary>
    public static RuleDenial? CheckEligible(BillingType billingType, long? clientId)
        => billingType != BillingType.Client || clientId is null
            ? RuleDenial.BusinessRule(ErrorCodes.PortalProjectNotEligible, "Only client projects have a client portal.")
            : null;

    /// <summary>Whether the content can change now: an archived project is read-only (it can still be viewed and downloaded).</summary>
    public static RuleDenial? CheckEditable(BillingType billingType, long? clientId, ProjectStatus status)
        => CheckEligible(billingType, clientId)
            ?? (status == ProjectStatus.Archived
                ? RuleDenial.BusinessRule(ErrorCodes.PortalContentReadOnly, "This project is archived, so its client portal files can't change. Reopen it to make changes.")
                : null);

    private static bool IsDangerous(byte[] b)
    {
        if (Ascii(b, 0, 2) == "MZ" || StartsWith(b, [0x7F, 0x45, 0x4C, 0x46]) || Ascii(b, 0, 2) == "#!")
        {
            return true;
        }

        var start = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 256)).TrimStart('﻿', ' ', '\t', '\r', '\n').ToLowerInvariant();
        return start.StartsWith("<!doctype html", StringComparison.Ordinal)
            || start.StartsWith("<html", StringComparison.Ordinal)
            || start.StartsWith("<svg", StringComparison.Ordinal)
            || start.StartsWith("<script", StringComparison.Ordinal);
    }

    /// <summary>ISO media (MP4, HEIC): a box of 4 size bytes, then "ftyp".</summary>
    private static bool IsoMedia(byte[] b) => Ascii(b, 4, 4) == "ftyp";

    private static bool Tiff(byte[] b) => StartsWith(b, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(b, [0x4D, 0x4D, 0x00, 0x2A]);

    private static bool Zip(byte[] b) => StartsWith(b, [0x50, 0x4B, 0x03, 0x04]) || StartsWith(b, [0x50, 0x4B, 0x05, 0x06]);

    /// <summary>A text format: no NUL bytes at the start.</summary>
    private static bool Text(byte[] b) => !b.AsSpan(0, Math.Min(b.Length, HeaderLength)).Contains((byte)0);

    private static bool StartsWith(byte[] b, byte[] prefix) => b.AsSpan().StartsWith(prefix);

    private static byte At(byte[] b, int index) => index < b.Length ? b[index] : (byte)0;

    private static string Ascii(byte[] b, int offset, int count)
        => offset >= b.Length ? string.Empty : Encoding.ASCII.GetString(b, offset, Math.Min(count, b.Length - offset));
}
