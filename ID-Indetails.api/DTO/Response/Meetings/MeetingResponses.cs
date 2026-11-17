namespace ID_Indetails.api.DTO.Response.Meetings;

public sealed record MeetingProjectResponse(long Id, string Title, string? Icon, string? Tint);

public sealed record MeetingPersonResponse(long Id, string Name);

/// <param name="Team">Their team's name, if they are in one.</param>
public sealed record MeetingInviteeResponse(long Id, string Name, long? PhotoFileId, string? Team);

/// <param name="Project">Null for a general meeting.</param>
/// <param name="Domain">The project's domain, or a general meeting's own; null for a company-wide meeting.</param>
/// <param name="CreatedBy">Null once the account that set it no longer exists.</param>
/// <param name="Attendees">A general meeting's people, or a project meeting's invitees (none when nobody was invited).</param>
public sealed record MeetingResponse(
    long Id,
    MeetingProjectResponse? Project,
    string? Domain,
    DateTime StartsAt,
    DateTime EndsAt,
    int Minutes,
    string? Topic,
    string? Description,
    MeetingPersonResponse? CreatedBy,
    IReadOnlyList<MeetingPersonResponse> Attendees,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
