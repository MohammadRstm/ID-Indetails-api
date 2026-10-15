using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

/// <summary>A project's files and its client portal: uploads on their way, the files in the store, the items that show them, and the portal's settings.</summary>
public interface IPortalContentRepository
{
    Task<PortalContentProject?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Every item of the project (removed ones too), by section, then order, then id.</summary>
    Task<IReadOnlyList<PortalItemView>> ItemsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The project's hero, introduction, revision and last publication, or the empty values before any change.</summary>
    Task<PortalSettingsView> SettingsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The project's settings row, created if missing, and locked until the transaction ends. Every change to the
    /// content takes this lock first, so changes and publications happen one at a time. Tracked.
    /// </summary>
    Task<PortalSettings> LockSettingsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Every item of the project, with its file. Tracked; take the settings lock first.</summary>
    Task<IReadOnlyList<PortalItem>> ItemsToChangeAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Files by id. Tracked.</summary>
    Task<IReadOnlyList<PortalFile>> FilesAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    Task<PortalFileView?> FileViewAsync(long fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a presentation shows: the draft (live items chosen for the client, by section and order) or the published
    /// copy (by published section and order, removed items included until the next publish).
    /// </summary>
    Task<IReadOnlyList<PresentationRow>> PresentationAsync(long projectId, bool published, CancellationToken cancellationToken = default);

    /// <summary>The published file of the item, when the item belongs to the project and is published.</summary>
    Task<PortalFileView?> PublishedFileAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>Whether a client account has live access to the Design project.</summary>
    Task<bool> ClientCanOpenAsync(long accountId, long projectId, CancellationToken cancellationToken = default);

    /// <summary>Whether any draft or published copy, or hero, still points at the file.</summary>
    Task<bool> IsReferencedAsync(long fileId, CancellationToken cancellationToken = default);

    Task<PortalItemView?> ItemViewAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>The project's live files: deleted files and unfinished uploads aren't counted.</summary>
    Task<PortalUsage> UsageAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Locks the item's row until the transaction ends. Tracked, with its file.</summary>
    Task<PortalItem?> GetItemForUpdateAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    Task<bool> ItemExistsAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>The highest place taken in the section, or null when it's empty.</summary>
    Task<int?> LastSortOrderAsync(long projectId, PortalSection section, CancellationToken cancellationToken = default);

    /// <summary>The person's upload started with this token, if any. Tracked.</summary>
    Task<PortalUpload?> FindUploadAsync(long createdById, string clientToken, CancellationToken cancellationToken = default);

    Task<PortalUpload?> GetUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default);

    /// <summary>Locks the upload's row until the transaction ends. Tracked.</summary>
    Task<PortalUpload?> GetUploadForUpdateAsync(long uploadId, CancellationToken cancellationToken = default);

    /// <summary>The id of the item showing the file stored under this key, if any.</summary>
    Task<long?> ItemIdForKeyAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Uploads that would replace the item's file, whatever their status. Tracked.</summary>
    Task<IReadOnlyList<PortalUpload>> UploadsReplacingAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Uploads still sending with nothing since <paramref name="cutoff"/>. Tracked.</summary>
    Task<IReadOnlyList<PortalUpload>> AbandonedUploadsAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default);

    /// <summary>Uploads that ended (any way) before <paramref name="cutoff"/>, oldest first. Tracked.</summary>
    Task<IReadOnlyList<PortalUpload>> EndedUploadsAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default);

    /// <summary>Files no longer used, whose objects are still to be deleted. Tracked.</summary>
    Task<IReadOnlyList<PortalFile>> FilesToDeleteAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Whether a live file uses this key.</summary>
    Task<bool> IsLiveFileKeyAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Every key a row knows about: files (deleted ones too) and uploads in any state.</summary>
    Task<HashSet<string>> KnownKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>The live files' keys with the size measured when they were stored.</summary>
    Task<Dictionary<string, long>> LiveFileSizesAsync(CancellationToken cancellationToken = default);

    void Add(PortalUpload upload);

    void Add(PortalFile file);

    void Add(PortalItem item);

    void Remove(PortalUpload upload);

    void Remove(PortalFile file);

    void Remove(PortalItem item);
}
