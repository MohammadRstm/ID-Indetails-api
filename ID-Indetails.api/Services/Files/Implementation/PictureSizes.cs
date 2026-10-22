using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// A stored file's content, or a smaller copy of a photo: made the first time it is asked for and kept beside the
/// original (<see cref="IFileStorage.SizedKey"/>), so a 72 px preview or a website card never downloads the whole photo.
/// Copies of <see cref="MarkedFrom"/> px and up carry the invisible <see cref="Watermark"/> (the website's are such);
/// the originals and the tiny previews don't.
/// </summary>
public sealed class PictureSizes(IFileStorage storage, IOptions<FileStorageOptions> options) : IPictureSizes
{
    /// <summary>Smaller copies are too small to carry the mark, and are only previews.</summary>
    public const int MarkedFrom = 480;

    public async Task<FileContent> OpenAsync(StoredFile file, int? size, CancellationToken cancellationToken = default)
        => size is { } wanted && IsPhoto(file) && await CopyAsync(file, Pictures.SizeFor(wanted), alwaysNew: false, cancellationToken) is { } copy
            ? copy
            // already that small, or not readable: the original is the copy
            : new FileContent(storage.OpenRead(file.StorageKey), file.MimeType, file.OriginalName);

    public async Task<FileContent?> OpenCopyAsync(StoredFile file, int size, CancellationToken cancellationToken = default)
        => IsPhoto(file) ? await CopyAsync(file, Pictures.SizeFor(size), alwaysNew: true, cancellationToken) : null;

    private static bool IsPhoto(StoredFile file) => file.MimeType.StartsWith("image/", StringComparison.Ordinal);

    /// <param name="alwaysNew">A new WebP even when the original is already that small: nothing of the original (such as
    /// the place a photo was taken) goes with it. A copy that carries the mark is always new.</param>
    private async Task<FileContent?> CopyAsync(StoredFile file, int kept, bool alwaysNew, CancellationToken cancellationToken)
    {
        var sized = storage.SizedKey(file.StorageKey, kept);
        var name = Path.ChangeExtension(file.OriginalName, Pictures.WebpExtension);
        if (storage.Exists(sized))
        {
            return new FileContent(storage.OpenRead(sized), Pictures.WebpMimeType, name);
        }

        byte[] original;
        await using (var content = storage.OpenRead(file.StorageKey))
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            original = buffer.ToArray();
        }

        Action<SKBitmap>? mark = kept >= MarkedFrom ? picture => Watermark.Embed(picture, file.Id, options.Value.WatermarkKey)
            : alwaysNew ? _ => { }
            : null;
        var smaller = Pictures.Shrink(original, kept, mark);
        if (smaller is null)
        {
            return null;
        }

        await storage.SaveAsAsync(sized, smaller, cancellationToken);
        return new FileContent(new MemoryStream(smaller), Pictures.WebpMimeType, name);
    }
}
