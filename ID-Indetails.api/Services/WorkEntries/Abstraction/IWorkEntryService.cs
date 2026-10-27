using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Logging, editing, deleting and restoring the caller's own work entries.</summary>
public interface IWorkEntryService
{
    Task<WorkEntryResponse> CreateAsync(SaveWorkEntryRequest request, CancellationToken cancellationToken = default);

    Task<WorkEntryResponse> UpdateAsync(long id, SaveWorkEntryRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<WorkEntryResponse> RestoreAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks anyone's entry as outside the project's original scope, or not (work_entry:view_all), after the edit
    /// window too; never in a closed month.
    /// </summary>
    Task<WorkEntryResponse> SetScopeAsync(long id, SetWorkEntryScopeRequest request, CancellationToken cancellationToken = default);
}
