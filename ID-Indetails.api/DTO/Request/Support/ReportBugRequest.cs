namespace ID_Indetails.api.DTO.Request.Support;

/// <summary>A message to the developer ("Contact the developer"), in the sender's words.</summary>
public sealed record ReportBugRequest
{
    /// <summary>What they were trying to do, and what happened.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Anything else that helps.</summary>
    public string? Details { get; init; }

    /// <summary>The client page it was sent from (or the error happened on): its path only, never its query.</summary>
    public string? Page { get; init; }

    /// <summary>Sent from the installed app rather than a browser tab; null when not known (sent through Proton).</summary>
    public bool? InstalledApp { get; init; }

    /// <summary>The error they came from, when they followed its "Contact the developer" link.</summary>
    public ContactErrorRequest? Error { get; init; }

    /// <summary>The requests that failed lately in this browser, newest first, so the log lines can be found.</summary>
    public IReadOnlyList<ReportedErrorRequest> RecentErrors { get; init; } = [];
}

/// <summary>The error a message is about: an API error's reference, or the browser's message for a page that failed.</summary>
public sealed record ContactErrorRequest
{
    /// <summary>The error response's trace id, or the reference shown to the person. Null for a page that failed in the browser.</summary>
    public string? Ref { get; init; }

    public DateTime At { get; init; }

    /// <summary>The browser's own message for a page that failed while drawing, when nothing reached the server.</summary>
    public string? Message { get; init; }
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
