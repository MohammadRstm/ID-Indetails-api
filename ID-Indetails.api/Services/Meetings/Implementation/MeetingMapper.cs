using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Repositories.Meetings.Abstraction;

namespace ID_Indetails.api.Services.Meetings.Implementation;

internal static class MeetingMapper
{
    public static MeetingResponse ToResponse(MeetingView m) => new(
        m.Id,
        new MeetingProjectResponse(m.ProjectId, m.ProjectTitle),
        m.StartsAt,
        m.EndsAt,
        (int)(m.EndsAt - m.StartsAt).TotalMinutes,
        m.Topic,
        m.Description,
        m.CreatedById is { } id && m.CreatedByName is { } name ? new MeetingPersonResponse(id, name) : null,
        m.CreatedAt,
        m.UpdatedAt);
}
