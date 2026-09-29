namespace ID_Indetails.api.DTO.Request.Messaging;

public sealed record SendMessageRequest
{
    public string Body { get; init; } = string.Empty;

    /// <summary>People to mention. Group chats only, and only the chat's members.</summary>
    public IReadOnlyList<long> MentionIds { get; init; } = [];
}

public sealed record MessagePageQuery
{
    /// <summary>Messages older than this one; the newest when not given.</summary>
    public long? Before { get; init; }

    public int Limit { get; init; } = 50;
}
