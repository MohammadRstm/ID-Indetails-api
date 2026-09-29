namespace ID_Indetails.api.DTO.Request.Search;

public sealed record SearchQuery
{
    /// <summary>Every word must match.</summary>
    public string Q { get; init; } = string.Empty;
}
