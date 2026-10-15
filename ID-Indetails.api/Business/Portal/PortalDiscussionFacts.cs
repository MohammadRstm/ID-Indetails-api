using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>A chat message as the discussion shows it, with its author's name. A deleted one has no text.</summary>
public sealed record DiscussionMessageRow(
    long Id, long? AuthorUserId, long? AuthorAccountId, string AuthorName, string? Body, DateTime CreatedAt, DateTime? EditedAt,
    DateTime? DeletedAt)
{
    public DiscussionPerson Author => DiscussionRows.Author(AuthorUserId, AuthorAccountId, AuthorName);
}

/// <summary>A comment as the discussion shows it, with its author's name.</summary>
public sealed record DiscussionCommentRow(
    long Id, long ItemId, long FileId, string ItemTitle, string FileName, long? AuthorUserId, long? AuthorAccountId,
    string AuthorName, string? Body, DateTime CreatedAt, DateTime? EditedAt, DateTime? DeletedAt)
{
    public DiscussionPerson Author => DiscussionRows.Author(AuthorUserId, AuthorAccountId, AuthorName);
}

/// <summary>
/// One file's comment thread at a glance, without any text: how many comments, the newest one (with the item's title
/// and file name as it was written about), and how many the reader hasn't read.
/// </summary>
public sealed record CommentThreadFacts(
    long ItemId, int Count, long LastCommentId, DateTime LastAt, string ItemTitle, string FileName, int Unread);

/// <summary>A staff member who may take part: a super admin always does, anyone else while they can see the project's domain.</summary>
public sealed record StaffCandidate(DiscussionPerson Person, bool IsSuperAdmin);

/// <summary>A file clients see now: what a comment on it is about.</summary>
public sealed record PublishedItemFacts(long ItemId, string Title, long FileId, string FileName, long UploadedById);

/// <summary>A client's notification, with its project's title.</summary>
public sealed record PortalNotificationRow(
    long Id, PortalNotificationKind Kind, long ProjectId, string ProjectTitle, long? SubjectId, string? Payload, int Count,
    DateTime CreatedAt, DateTime? ReadAt);

internal static class DiscussionRows
{
    public static DiscussionPerson Author(long? userId, long? accountId, string name)
        => userId is { } staff ? new(DiscussionSide.Staff, staff, name) : new(DiscussionSide.Client, accountId!.Value, name);
}
