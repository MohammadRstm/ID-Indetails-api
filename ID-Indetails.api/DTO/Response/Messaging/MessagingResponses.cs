using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Messaging;

/// <param name="PhotoFileId">A message's sender's photo; not given for mentions.</param>
public sealed record ChatPersonResponse(long Id, string Name, long? PhotoFileId = null);

public sealed record ChatTeamResponse(long Id, string Name, string Domain);

/// <summary>Someone the caller can message directly, or a member of a team chat.</summary>
/// <param name="PhotoFileId">Their profile photo, read through <c>GET /api/files/{id}</c>.</param>
public sealed record ContactResponse(
    long Id,
    string Name,
    string Role,
    string? Domain,
    TeamSummaryResponse? Team,
    string? Position,
    long? PhotoFileId);

/// <summary>The work entry a shared comment is about, as it is now.</summary>
/// <param name="Attachment">The entry's photo or video, if it has one.</param>
public sealed record TaskCommentResponse(
    long EntryId,
    long CommentId,
    ChatPersonResponse Author,
    long ProjectId,
    string ProjectTitle,
    string Description,
    DateTime StartedAt,
    int Minutes,
    FileResponse? Attachment);

/// <param name="Kind"><c>text</c>, or <c>task_comment</c> for a shared work-entry comment.</param>
/// <param name="TaskComment">For <c>task_comment</c> messages; null once the entry is deleted.</param>
public sealed record MessageResponse(
    long Id,
    ChatPersonResponse Sender,
    string? Body,
    string Kind,
    IReadOnlyList<ChatPersonResponse> Mentions,
    IReadOnlyList<FileResponse> Attachments,
    TaskCommentResponse? TaskComment,
    DateTime CreatedAt);

/// <summary>Oldest first. <paramref name="HasMore"/> says older messages exist.</summary>
public sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, bool HasMore);

/// <summary>A team chat (<c>group</c>, with <paramref name="Team"/>) or a direct chat (<c>dm</c>, with <paramref name="Person"/>).</summary>
public sealed record ConversationResponse(
    ConversationType Type,
    ChatTeamResponse? Team,
    ContactResponse? Person,
    MessageResponse? LastMessage,
    int Unread,
    bool UnreadMention);

/// <summary>For the header: every unread message, and whether any of them mentions the caller.</summary>
public sealed record MessagesBadgeResponse(int Unread, bool Mention);
