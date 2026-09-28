using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Reading work entries: everyone's (for people who see all Design work) and the caller's own.</summary>
public interface IWorkEntryQueryService
{
    Task<PagedResponse<WorkEntryResponse>> ListAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default);

    Task<PagedResponse<WorkEntryResponse>> ListMineAsync(MyWorkEntryListQuery query, CancellationToken cancellationToken = default);

    Task<WorkEntryDetailResponse> GetAsync(long id, CancellationToken cancellationToken = default);
}
