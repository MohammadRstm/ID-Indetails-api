using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Messaging;

/// <param name="PhotoFileId">A message's sender's photo; not given for mentions.</param>
public sealed record ChatPersonResponse(long Id, string Name, long? PhotoFileId = null);

public sealed record ChatTeamResponse(long Id, string Name, string Domain);

/// <summary>Someone the caller can message directly, or a member of a team chat.</summary>
/// <param name="PhotoFileId">Their profile photo, read through <c>GET /api/files/{id}</c>.</param>
/// <param name="Online">Whether they have the app open now.</param>
/// <param name="LastSeenAt">When they last used the app (recorded at most every few minutes, and when they close it).</param>
public sealed record ContactResponse(
    long Id,
    string Name,
    string Role,
    string? Domain,
    TeamSummaryResponse? Team,
    string? Position,
    long? PhotoFileId,
    bool Online = false,
    DateTime? LastSeenAt = null);

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
    int? Minutes,
    FileResponse? Attachment);

/// <summary>A message's ticks and how many of the people it is for have received and seen it.</summary>
/// <param name="Delivered">How many have received it, those who have seen it included.</param>
/// <param name="Of">How many it is for: everyone in the chat but its sender.</param>
public sealed record MessageReceiptResponse(ReceiptStatus Status, int Seen, int Delivered, int Of);

/// <param name="At">When they saw it, where known: always in Announcements, otherwise only for the last message they read.</param>
public sealed record ReceiptPersonResponse(long Id, string Name, long? PhotoFileId, DateTime? At = null);

/// <summary>Who has seen a message, who has received it without reading it yet, and who hasn't received it.</summary>
public sealed record MessageReceiptsResponse(
    long MessageId,
    MessageReceiptResponse Summary,
    IReadOnlyList<ReceiptPersonResponse> Seen,
    IReadOnlyList<ReceiptPersonResponse> Delivered,
    IReadOnlyList<ReceiptPersonResponse> NotReceived);

/// <param name="Kind"><c>text</c>, or <c>task_comment</c> for a shared work-entry comment.</param>
/// <param name="TaskComment">For <c>task_comment</c> messages; null once the entry is deleted.</param>
/// <param name="Receipt">
/// Only for those who see it: the sender of their own messages, the super admins in team chats, and whoever may
/// announce in Announcements. Null for everyone else.
/// </param>
public sealed record MessageResponse(
    long Id,
    ChatPersonResponse Sender,
    string? Body,
    string Kind,
    IReadOnlyList<ChatPersonResponse> Mentions,
    IReadOnlyList<FileResponse> Attachments,
    TaskCommentResponse? TaskComment,
    DateTime CreatedAt,
    bool ViaProton = false,
    MessageReceiptResponse? Receipt = null);

/// <summary>Oldest first. <paramref name="HasMore"/> says older messages exist.</summary>
public sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, bool HasMore);

/// <summary>A custom chat group, as its members see it in their list of chats.</summary>
/// <param name="PhotoFileId">Its photo, read through <c>GET /api/files/{id}</c>; null shows the people icon.</param>
/// <param name="MemberCount">Its active members, the caller included.</param>
public sealed record ChatGroupResponse(long Id, string Name, long? PhotoFileId, int MemberCount);

/// <summary>A custom group for whoever manages groups (<c>message:manage_groups</c>), with its active members.</summary>
/// <param name="IsMember">Whether the caller is in it: only members read its messages.</param>
public sealed record ChatGroupDetailResponse(
    long Id,
    string Name,
    long? PhotoFileId,
    IReadOnlyList<ContactResponse> Members,
    bool IsMember,
    DateTime CreatedAt);

/// <summary>
/// A team chat (<c>group</c>, with <paramref name="Team"/>), a direct chat (<c>dm</c>, with <paramref name="Person"/>),
/// Announcements, or a custom group (<c>custom_group</c>, with <paramref name="Group"/>).
/// </summary>
/// <param name="OnlineCount">Team chats and custom groups: how many of the others in it have the app open now.</param>
public sealed record ConversationResponse(
    ConversationType Type,
    ChatTeamResponse? Team,
    ContactResponse? Person,
    MessageResponse? LastMessage,
    int Unread,
    bool UnreadMention,
    int? OnlineCount = null,
    ChatGroupResponse? Group = null);

/// <summary>For the header: every unread message, and whether any of them mentions the caller.</summary>
public sealed record MessagesBadgeResponse(int Unread, bool Mention);
