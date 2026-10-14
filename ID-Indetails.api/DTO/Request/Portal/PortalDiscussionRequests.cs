namespace ID_Indetails.api.DTO.Request.Portal;

/// <summary>A new chat message. The client token (a UUID made by the browser) makes sending it twice harmless.</summary>
public sealed record PortalMessageRequest
{
    public string Body { get; init; } = string.Empty;

    public string ClientToken { get; init; } = string.Empty;

    /// <summary>The keys of the people mentioned ("u12" staff, "c7" clients).</summary>
    public IReadOnlyList<string> Mentions { get; init; } = [];
}

/// <summary>A new comment on a published file.</summary>
public sealed record PortalCommentRequest
{
    public long ItemId { get; init; }

    public string Body { get; init; } = string.Empty;

    public string ClientToken { get; init; } = string.Empty;

    public IReadOnlyList<string> Mentions { get; init; } = [];
}

/// <summary>New text for one's own message or comment.</summary>
public sealed record PortalEditRequest
{
    public string Body { get; init; } = string.Empty;

    public IReadOnlyList<string> Mentions { get; init; } = [];
}

/// <summary>How far the person has read: the newest chat message they have seen, and/or the newest comment on one file.</summary>
public sealed record PortalReadRequest
{
    public long? LastMessageId { get; init; }

    /// <summary>The file whose comments were read; goes with <see cref="LastCommentId"/>.</summary>
    public long? ItemId { get; init; }

    public long? LastCommentId { get; init; }
}

/// <summary>Marks some of a client's notifications read, or all of them.</summary>
public sealed record PortalNotificationsReadRequest
{
    public IReadOnlyList<long>? Ids { get; init; }

    public bool All { get; init; }
}
