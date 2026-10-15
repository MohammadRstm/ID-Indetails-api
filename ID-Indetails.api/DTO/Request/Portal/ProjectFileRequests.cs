namespace ID_Indetails.api.DTO.Request.Portal;

/// <summary>Chooses a file for the client, or takes it back: the next publish shows or hides it.</summary>
public sealed record ProjectFileForClientRequest
{
    public bool ForClient { get; init; }
}

/// <summary>A comment's text, new or edited: plain text.</summary>
public sealed record ProjectFileCommentRequest
{
    public string Body { get; init; } = string.Empty;
}

/// <summary>Marks a comment fixed, or open again.</summary>
public sealed record ProjectFileFixedRequest
{
    public bool Fixed { get; init; }
}
