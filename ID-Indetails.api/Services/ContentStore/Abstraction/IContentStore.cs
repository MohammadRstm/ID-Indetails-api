namespace ID_Indetails.api.Services.ContentStore.Abstraction;

/// <summary>
/// Private object storage for client portal content: Cloudflare R2 in production, a folder on disk in development and
/// tests (never in production). The bytes never pass through the API: the browser uploads each part of a file to a
/// short-lived signed link, and reads the file through another. Links are only handed out after the caller's access
/// has been checked; keys are always server-generated and never contain a file name.
/// </summary>
public interface IContentStore
{
    /// <summary>False when no store is configured (ContentStore:Provider is None); every other member then throws a 503.</summary>
    bool IsAvailable { get; }

    /// <summary>Starts a multipart upload of a new object under <paramref name="key"/>, stored with this content type. Returns the store's upload id.</summary>
    Task<string> StartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default);

    /// <summary>A signed link the browser PUTs the raw bytes of one part to, without any other header, until <paramref name="expiresAt"/>.</summary>
    Task<string> GetPartUploadUrlAsync(string key, string uploadId, int partNumber, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>The parts received so far, by part number. Null when the store has no such upload (already completed, or aborted).</summary>
    Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Joins the parts, in order, into the object. An upload that no longer exists (joined by another attempt a moment
    /// ago, or abandoned) is not an error: <see cref="GetSizeAsync"/> then says whether the object is there.
    /// </summary>
    Task CompleteUploadAsync(string key, string uploadId, IReadOnlyList<StoredPart> parts, CancellationToken cancellationToken = default);

    /// <summary>Abandons an upload and frees its parts. An upload that no longer exists is not an error.</summary>
    Task AbortUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default);

    /// <summary>The stored object's size in bytes, as the store reports it, or null when there is no such object.</summary>
    Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Up to <paramref name="count"/> bytes from the start of the object: to check what the file really is, or to read a
    /// file small enough to hold in memory whole (for Proton to read it).
    /// </summary>
    Task<byte[]> ReadStartAsync(string key, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// A signed link that reads the object (byte ranges allowed) until <paramref name="expiresAt"/>, served with this
    /// content type, as <c>inline</c> or <c>attachment</c> under <paramref name="fileName"/>, and with
    /// <see cref="ContentLinks.CacheControl"/>.
    /// </summary>
    Task<string> GetDownloadUrlAsync(
        string key, string fileName, string contentType, bool inline, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Deletes the object. A missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Every object whose key starts with <paramref name="prefix"/>, page by page (the weekly check against the database).</summary>
    IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken = default);
}

public static class ContentLinks
{
    /// <summary>The browser may keep what a link loaded for as long as the link has left, never longer.</summary>
    public static string CacheControl(DateTime expiresAt, DateTime now)
        => $"private, max-age={Math.Max(0, (long)(expiresAt - now).TotalSeconds)}";
}

/// <summary>One uploaded part of a multipart upload.</summary>
public sealed record StoredPart(int PartNumber, long Size, string ETag);

/// <summary>One object in the store.</summary>
public sealed record StoredObject(string Key, long Size, DateTime LastModifiedUtc);
