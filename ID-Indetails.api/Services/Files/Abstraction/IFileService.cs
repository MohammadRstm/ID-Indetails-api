using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Files.Abstraction;

public sealed record FileContent(Stream Content, string MimeType, string Name);

/// <summary>
/// Uploads are made first and attached afterwards (to a message, a work entry, a profile or the company). Who may
/// download a file follows what it is attached to.
/// </summary>
public interface IFileService
{
    /// <param name="content">The file as it arrives; it is read once, from the start.</param>
    /// <param name="uploadLength">The request's length, to refuse an upload over the day's allowance before storing it.</param>
    Task<FileResponse> UploadAsync(Stream content, string? fileName, long uploadLength, CancellationToken cancellationToken = default);

    /// <summary>
    /// The file's content, if the caller may read it; otherwise not found. A photo with a <paramref name="size"/> comes
    /// as a smaller copy (<see cref="IPictureSizes"/>).
    /// </summary>
    Task<FileContent> OpenAsync(long id, int? size = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes uploads that were attached to nothing within a day, and returns how many went.</summary>
    Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Taking an upload to attach it. Kept apart from <see cref="IFileService"/>, which reads through what files are attached to.</summary>
public interface IFileClaims
{
    /// <summary>
    /// A file the caller uploaded and hasn't attached to anything yet, locked until the caller's transaction ends so it
    /// can't be attached twice. <paramref name="kind"/>, when given, is the kind it must be.
    /// </summary>
    Task<StoredFile> ClaimAsync(long id, FileKind? kind, CancellationToken cancellationToken = default);
}
