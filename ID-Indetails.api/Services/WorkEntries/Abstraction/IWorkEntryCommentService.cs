using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

public interface IWorkEntryCommentService
{
    Task<IReadOnlyList<WorkEntryCommentResponse>> ListAsync(long workEntryId, CancellationToken cancellationToken = default);

    Task<WorkEntryCommentResponse> AddAsync(long workEntryId, AddWorkEntryCommentRequest request, CancellationToken cancellationToken = default);
}
