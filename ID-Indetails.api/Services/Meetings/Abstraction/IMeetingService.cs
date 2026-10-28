using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Meetings;

namespace ID_Indetails.api.Services.Meetings.Abstraction;

public interface IMeetingService
{
    Task<IReadOnlyList<MeetingResponse>> ListAsync(MeetingListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MeetingResponse>> ListForProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<MeetingResponse> CreateAsync(long projectId, SaveMeetingRequest request, CancellationToken cancellationToken = default);

    /// <summary>A meeting about no project, for the people chosen (see <c>MeetingRules</c>).</summary>
    Task<MeetingResponse> CreateGeneralAsync(SaveGeneralMeetingRequest request, CancellationToken cancellationToken = default);

    Task<MeetingResponse> UpdateAsync(long id, SaveMeetingRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
