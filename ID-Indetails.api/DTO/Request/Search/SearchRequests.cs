namespace ID_Indetails.api.DTO.Request.Search;

/// <summary>The sidebar's counts are for one domain.</summary>
public sealed record NavCountsQuery
{
    public string Domain { get; init; } = string.Empty;
}

public sealed record SearchQuery
{
    /// <summary>Every word must match.</summary>
    public string Q { get; init; } = string.Empty;
}
