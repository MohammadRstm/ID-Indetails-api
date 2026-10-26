namespace ID_Indetails.api.DTO.Request.Execution;

/// <summary><c>?ids=1&amp;ids=2</c>: the sites shown, typically one page of the project list.</summary>
public sealed record SiteSummaryQuery
{
    public long[] Ids { get; init; } = [];
}

/// <summary>What happened on site, in the writer's words.</summary>
public sealed record SaveSiteLogRequest
{
    public string Body { get; init; } = string.Empty;
}

/// <summary>Daily logs, newest first. Days are company days, both inclusive.</summary>
public sealed record SiteLogListQuery
{
    /// <summary>Everyone's list only: one person's logs.</summary>
    public long? UserId { get; init; }

    public long? ProjectId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>Words of the log or the site's title.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
