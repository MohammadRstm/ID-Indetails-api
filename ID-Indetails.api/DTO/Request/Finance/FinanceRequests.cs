namespace ID_Indetails.api.DTO.Request.Finance;

/// <summary>Company-time-zone days, both inclusive. Defaults to this quarter so far; never past today.</summary>
public sealed record FinancePeriodQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

public sealed record FinanceMonthsQuery
{
    /// <summary>How many months, ending with this one.</summary>
    public int Months { get; init; } = 12;
}
