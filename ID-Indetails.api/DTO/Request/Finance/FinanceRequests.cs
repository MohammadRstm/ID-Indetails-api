namespace ID_Indetails.api.DTO.Request.Finance;

/// <summary>Company-time-zone days, both inclusive. Defaults to this quarter so far; never past today.</summary>
public sealed record FinancePeriodQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <summary>The summary's period (as <see cref="FinancePeriodQuery"/>) and the months of the monthly and billing figures.</summary>
public sealed record FinanceOverviewQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>How many months, ending with this one.</summary>
    public int Months { get; init; } = 12;
}

public sealed record FinanceMonthsQuery
{
    /// <summary>How many months, ending with this one.</summary>
    public int Months { get; init; } = 12;
}
