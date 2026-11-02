namespace ID_Indetails.api.DTO.Request.Support;

/// <summary>A bug a signed-in person ran into, in their words.</summary>
public sealed record ReportBugRequest
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Steps { get; init; }

    /// <summary>The client page it happened on: its path only, never its query.</summary>
    public string? Page { get; init; }

    /// <summary>The requests that failed lately in this browser, newest first, so the log lines can be found.</summary>
    public IReadOnlyList<ReportedErrorRequest> RecentErrors { get; init; } = [];
}

public sealed record ReportedErrorRequest
{
    /// <summary>The error response's trace id.</summary>
    public string Ref { get; init; } = string.Empty;

    public int Status { get; init; }

    /// <summary>The API path asked for, without its query.</summary>
    public string? Path { get; init; }

    public DateTime At { get; init; }
}
