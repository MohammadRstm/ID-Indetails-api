namespace ID_Indetails.api.DTO.Request.Messaging;

public sealed record SendMessageRequest
{
    /// <summary>May be empty when the message carries attachments.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Photos or videos the caller uploaded (<c>POST /api/files</c>) and hasn't attached elsewhere.</summary>
    public IReadOnlyList<long> AttachmentIds { get; init; } = [];

    /// <summary>People to mention. Group chats only, and only the chat's members.</summary>
    public IReadOnlyList<long> MentionIds { get; init; } = [];
}

public sealed record MessagePageQuery
{
    /// <summary>Messages older than this one; the newest when not given.</summary>
    public long? Before { get; init; }

    public int Limit { get; init; } = 50;
}

/// <summary>A new custom chat group. Whoever creates it is put in it too.</summary>
public sealed record CreateChatGroupRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Active staff of any business; the creator may be left out, as they join anyway.</summary>
    public IReadOnlyList<long> MemberIds { get; init; } = [];
}

public sealed record RenameChatGroupRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed record AddChatGroupMembersRequest
{
    /// <summary>Active staff of any business. Anyone already in the group is left as they are.</summary>
    public IReadOnlyList<long> UserIds { get; init; } = [];
}
