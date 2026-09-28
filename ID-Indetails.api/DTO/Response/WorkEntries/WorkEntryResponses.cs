namespace ID_Indetails.api.DTO.Response.WorkEntries;

public sealed record WorkEntryProjectResponse(long Id, string Title);

public sealed record WorkEntryPersonResponse(long Id, string Name);

/// <param name="CanEdit">Whether the caller may still edit or delete it: their own entry, within the edit window, on a project that accepts work.</param>
public sealed record WorkEntryResponse(
    long Id,
    WorkEntryPersonResponse Person,
    WorkEntryProjectResponse Project,
    string Description,
    DateTime StartedAt,
    DateTime EndedAt,
    int Minutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool CanEdit,
    int CommentCount);

/// <param name="Role">super_admin, admin or employee, for the badge next to the name.</param>
public sealed record WorkEntryCommentAuthorResponse(long Id, string Name, string Role);

public sealed record WorkEntryCommentResponse(long Id, WorkEntryCommentAuthorResponse Author, string Body, bool SentToGroup, DateTime CreatedAt);

/// <param name="SameDay">The same person's other entries on the same project that day (company time zone), oldest first.</param>
public sealed record WorkEntryDetailResponse(
    WorkEntryResponse Entry,
    IReadOnlyList<WorkEntryCommentResponse> Comments,
    IReadOnlyList<WorkEntryResponse> SameDay);
