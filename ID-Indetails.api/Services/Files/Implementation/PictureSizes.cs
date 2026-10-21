using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Files.Abstraction;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// A stored file's content, or a smaller copy of a photo: made the first time it is asked for and kept beside the
/// original (<see cref="IFileStorage.SizedKey"/>), so a 72 px preview or a website card never downloads the whole photo.
/// </summary>
public sealed class PictureSizes(IFileStorage storage) : IPictureSizes
{
    public async Task<FileContent> OpenAsync(StoredFile file, int? size, CancellationToken cancellationToken = default)
    {
        if (size is not { } wanted || !file.MimeType.StartsWith("image/", StringComparison.Ordinal))
        {
            return new FileContent(storage.OpenRead(file.StorageKey), file.MimeType, file.OriginalName);
        }

        var sized = storage.SizedKey(file.StorageKey, Pictures.SizeFor(wanted));
        if (!storage.Exists(sized))
        {
            byte[] original;
            await using (var content = storage.OpenRead(file.StorageKey))
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                original = buffer.ToArray();
            }

            // already that small, or not readable: the original is the copy
            var smaller = Pictures.Shrink(original, Pictures.SizeFor(wanted));
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
