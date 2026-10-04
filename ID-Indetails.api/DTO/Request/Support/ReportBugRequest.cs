namespace ID_Indetails.api.DTO.Request.Support;

/// <summary>A bug a signed-in person ran into, in their words.</summary>
public sealed record ReportBugRequest
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Steps { get; init; }

    /// <summary>The client page it happened on: its path only, never its query.</summary>
    public string? Page { get; init; }
}
