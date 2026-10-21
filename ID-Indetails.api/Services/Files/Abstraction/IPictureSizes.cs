using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Files.Abstraction;

/// <summary>A stored file's content, or a smaller copy of a photo; see docs/files.md.</summary>
public interface IPictureSizes
{
    /// <summary>
    /// The file itself; or, for a photo with a <paramref name="size"/>, a copy at most that big on its longest side
    /// (rounded up to one of the sizes kept), made once and kept. A photo already that small comes as it is.
    /// </summary>
    Task<FileContent> OpenAsync(StoredFile file, int? size, CancellationToken cancellationToken = default);

    /// <summary>
    /// Only ever a copy, never the file itself: re-encoded at most <paramref name="size"/> on its longest side, with
    /// nothing of the original's camera data, marked from 480 px. Null when no copy can be made (a photo the server
    /// can't read, such as HEIC, or one too large to read). For what the public sees.
    /// </summary>
    Task<FileContent?> OpenCopyAsync(StoredFile file, int size, CancellationToken cancellationToken = default);
}
