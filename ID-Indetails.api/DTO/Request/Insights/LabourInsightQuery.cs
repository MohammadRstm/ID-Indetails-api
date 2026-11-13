namespace ID_Indetails.api.DTO.Request.Insights;

/// <summary>Which months of Design labour to show.</summary>
public sealed record LabourInsightQuery
{
    /// <summary>How many months, ending with this one: 1 to 24, 12 when left out.</summary>
    public int? Months { get; init; }
}
