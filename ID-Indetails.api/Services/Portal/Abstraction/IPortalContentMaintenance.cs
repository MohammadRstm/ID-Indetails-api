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
}
