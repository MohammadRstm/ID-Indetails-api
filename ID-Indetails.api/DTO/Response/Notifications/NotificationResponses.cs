using System.Text.Json;

namespace ID_Indetails.api.DTO.Response.Notifications;

public sealed record NotificationRefResponse(long Id, string Name, long? PhotoFileId);

public sealed record NotificationProjectResponse(long Id, string Title, string Domain, string? Icon, string? Tint);

/// <summary>The client writes the text and the link from the kind, the subject and the payload.</summary>
/// <param name="Count">For rolling digests, how many things it sums up; otherwise 1.</param>
public sealed record NotificationResponse(
    long Id,
    string Kind,
    NotificationRefResponse? Actor,
    NotificationProjectResponse? Project,
    string? SubjectType,
    long? SubjectId,
    JsonElement? Payload,
    int Count,
    DateTime CreatedAt,
    DateTime? ReadAt);

public sealed record UnreadCountResponse(int Unread);
