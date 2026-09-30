namespace ID_Indetails.api.DTO.Response.Meetings;

public sealed record MeetingProjectResponse(long Id, string Title, string? Icon, string? Tint);

public sealed record MeetingPersonResponse(long Id, string Name);

/// <param name="CreatedBy">Null once the account that set it no longer exists.</param>
public sealed record MeetingResponse(
    long Id,
    MeetingProjectResponse Project,
    DateTime StartsAt,
    DateTime EndsAt,
    int Minutes,
    string? Topic,
    string? Description,
    MeetingPersonResponse? CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
