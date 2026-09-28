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
