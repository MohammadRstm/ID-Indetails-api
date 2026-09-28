namespace ID_Indetails.api.DTO.Request.Execution;

/// <summary><c>?ids=1&amp;ids=2</c>: the sites shown, typically one page of the project list.</summary>
public sealed record SiteSummaryQuery
{
    public long[] Ids { get; init; } = [];
}
