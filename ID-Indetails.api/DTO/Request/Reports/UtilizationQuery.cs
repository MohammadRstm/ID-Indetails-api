namespace ID_Indetails.api.DTO.Request.Reports;

/// <summary>Which month's utilization to show.</summary>
public sealed record UtilizationQuery
{
    /// <summary>Any day of the month; this month when left out. Never a future month.</summary>
    public DateOnly? Month { get; init; }
}
