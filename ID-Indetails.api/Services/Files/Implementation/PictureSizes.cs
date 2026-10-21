using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.Options;

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
    {
        if (size is not { } wanted || !file.MimeType.StartsWith("image/", StringComparison.Ordinal))
        {
            return new FileContent(storage.OpenRead(file.StorageKey), file.MimeType, file.OriginalName);
        }

        var kept = Pictures.SizeFor(wanted);
        var sized = storage.SizedKey(file.StorageKey, kept);
        if (!storage.Exists(sized))
        {
            byte[] original;
            await using (var content = storage.OpenRead(file.StorageKey))
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                original = buffer.ToArray();
            }

            // already that small, or not readable: the original is the copy (a copy that carries the mark is always new)
            var smaller = Pictures.Shrink(original, kept, kept >= MarkedFrom ? picture => Watermark.Embed(picture, file.Id, options.Value.WatermarkKey) : null);
            if (smaller is null)
            {
                return new FileContent(new MemoryStream(original), file.MimeType, file.OriginalName);
            }

            await storage.SaveAsAsync(sized, smaller, cancellationToken);
            return new FileContent(new MemoryStream(smaller), Pictures.WebpMimeType, Path.ChangeExtension(file.OriginalName, Pictures.WebpExtension));
        }

        return new FileContent(storage.OpenRead(sized), Pictures.WebpMimeType, Path.ChangeExtension(file.OriginalName, Pictures.WebpExtension));
    }
}
