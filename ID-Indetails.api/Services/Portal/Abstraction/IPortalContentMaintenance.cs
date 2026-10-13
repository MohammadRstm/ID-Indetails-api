using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>Keeps the content store and the rows in step: what the background jobs run.</summary>
public interface IPortalContentMaintenance
{
    /// <summary>
    /// Removes abandoned uploads and what reached the store, deletes the objects of files no longer used, and forgets
    /// uploads that ended a week ago. Returns how many it handled.
    /// </summary>
    Task<int> CleanUpAsync(CancellationToken cancellationToken = default);

    /// <summary>Compares the store with the rows and logs what differs; deletes unknown objects only when switched on.</summary>
    Task ReconcileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Best effort, right after a change was saved: deletes the objects of files it let go, then their rows. What fails
    /// here, the cleanup does later.
    /// </summary>
    Task DeleteReleasedAsync(IReadOnlyList<PortalFile> files, CancellationToken cancellationToken = default);

    /// <summary>Best effort: drops an ended upload's parts and object. What fails here, the cleanup does later.</summary>
    Task RemoveUploadAsync(PortalUpload upload, CancellationToken cancellationToken = default);
}
