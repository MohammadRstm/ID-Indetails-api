using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Portal;

/// <summary>Someone in a project's discussion. <see cref="Key"/> ("u12" staff, "c7" clients) is what mentions use.</summary>
public sealed record DiscussionPersonResponse(string Key, string Name, string Kind, bool IsYou);

/// <summary>A project's discussion at a glance: whether it can be written in, what is unread, who takes part, and its comment threads.</summary>
public sealed record DiscussionSummaryResponse
{
    public required long ProjectId { get; init; }

    public required bool CanWrite { get; init; }

    public required string? ClosedReason { get; init; }

    public required int UnreadMessages { get; init; }

    public required int UnreadComments { get; init; }

    public required long? LastMessageId { get; init; }

    public required long? LastCommentId { get; init; }

    public required IReadOnlyList<DiscussionPersonResponse> Participants { get; init; }

    /// <summary>Files with comments, newest activity first.</summary>
    public required IReadOnlyList<CommentThreadSummaryResponse> Threads { get; init; }
}

/// <summary>One file's thread. <see cref="Available"/> is false once the file is no longer shared: it can be read, not added to.</summary>
public sealed record CommentThreadSummaryResponse(
    long ItemId, string Title, string FileName, bool Available, int Count, int Unread, DateTime LastAt);

public sealed record PortalChatMessageResponse
{
    public required long Id { get; init; }

    public required DiscussionPersonResponse Author { get; init; }

    /// <summary>Null once deleted.</summary>
    public required string? Body { get; init; }

    /// <summary>Deleted for everyone by its author: it keeps its place without its text or mentions.</summary>
    public required bool Deleted { get; init; }

    public required IReadOnlyList<string> Mentions { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime? EditedAt { get; init; }

    /// <summary>The caller's own message, not deleted, in a discussion that can be written in: it can be edited or deleted.</summary>
    public required bool CanEdit { get; init; }
}

/// <summary>A page of the chat, oldest first.</summary>
public sealed record PortalMessagePageResponse(IReadOnlyList<PortalChatMessageResponse> Messages, bool HasMore);

public sealed record PortalCommentResponse
{
    public required long Id { get; init; }

    public required long ItemId { get; init; }

    public required DiscussionPersonResponse Author { get; init; }

    /// <summary>Null once deleted.</summary>
    public required string? Body { get; init; }

    public required bool Deleted { get; init; }

    public required IReadOnlyList<string> Mentions { get; init; }

    /// <summary>The file version the comment was written about.</summary>
    public required string FileName { get; init; }

    /// <summary>The file was replaced since: the comment is about an earlier version.</summary>
    public required bool OnEarlierVersion { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime? EditedAt { get; init; }

    public required bool CanEdit { get; init; }

    public required bool CanDelete { get; init; }
}

/// <summary>A file's comments, oldest first.</summary>
public sealed record PortalCommentThreadResponse(
    long ItemId, string Title, string FileName, bool Available, IReadOnlyList<PortalCommentResponse> Comments);

/// <summary>A client's notification: who wrote, where; never what.</summary>
public sealed record PortalNotificationResponse
{
    public required long Id { get; init; }

    public required PortalNotificationKind Kind { get; init; }

    public required long ProjectId { get; init; }

    public required string ProjectTitle { get; init; }

    public required string ActorName { get; init; }

    public required long? ItemId { get; init; }

    public required string? ItemTitle { get; init; }

    public required int Count { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required bool Read { get; init; }
}

public sealed record PortalNotificationPageResponse(IReadOnlyList<PortalNotificationResponse> Items, bool HasMore);

public sealed record PortalUnreadResponse(int Unread);
