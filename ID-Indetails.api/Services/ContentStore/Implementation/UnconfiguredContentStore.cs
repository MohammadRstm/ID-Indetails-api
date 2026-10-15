using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.ContentStore.Abstraction;

namespace ID_Indetails.api.Services.ContentStore.Implementation;

/// <summary>
/// The store while <c>ContentStore:Provider</c> is None: the app runs, and everything that needs the store answers 503.
/// <see cref="ListAsync"/> throws as well, as soon as it is called; the weekly check looks at <see cref="IsAvailable"/> first.
/// </summary>
public sealed class UnconfiguredContentStore : IContentStore
{
    public bool IsAvailable => false;

    public Task<string> StartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task<string> GetPartUploadUrlAsync(string key, string uploadId, int partNumber, DateTime expiresAt, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task CompleteUploadAsync(string key, string uploadId, IReadOnlyList<StoredPart> parts, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task AbortUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task<byte[]> ReadStartAsync(string key, int count, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task<string> GetDownloadUrlAsync(
        string key, string fileName, string contentType, bool inline, DateTime expiresAt, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => throw Unavailable();

    public IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken = default)
        => throw Unavailable();

    private static ServiceUnavailableException Unavailable()
        => new(ErrorCodes.ContentStorageUnavailable, "Client portal file storage isn't set up yet. Ask your administrator to configure it.");
}
