using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Reading work entries: everyone's (for people who see all Design work) and the caller's own.</summary>
public interface IWorkEntryQueryService
{
    Task<PagedResponse<WorkEntryResponse>> ListAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default);

    Task<PagedResponse<WorkEntryResponse>> ListMineAsync(MyWorkEntryListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own entries deleted lately (see <see cref="Implementation.WorkEntryQueryService.RestorableFor"/>), most
    /// recently deleted first: the ones <see cref="IWorkEntryService.RestoreAsync"/> may bring back (<c>canEdit</c> says
    /// whether it still may). The app lists none: it restores right after a delete.
    /// </summary>
    Task<IReadOnlyList<WorkEntryResponse>> ListMyDeletedAsync(CancellationToken cancellationToken = default);

    Task<WorkEntryDetailResponse> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Everyone's matching entries as CSV, oldest first, ignoring paging.</summary>
    Task<WorkEntryExport> ExportAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default);
}

public sealed record WorkEntryExport(string FileName, byte[] Content);
