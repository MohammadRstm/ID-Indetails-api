using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Insights;

/// <summary>The changes to simulate on one project; at least one. Nothing is saved.</summary>
public sealed record ProjectScenarioQuery
{
    /// <summary>A variation order on the work (a site) or the price (Design), in USD net of TVA; negative takes work out.</summary>
    public decimal? VoAmount { get; init; }

    /// <summary>An extra cost in USD, with its source.</summary>
    public decimal? ExtraCost { get; init; }

    /// <summary>Design: labour or expenses. A site: site_expense or ground_labour.</summary>
    public CostSource? ExtraCostSource { get; init; }

    /// <summary>A site's supervision rate (0.10 is 10%).</summary>
    public decimal? SupervisionRate { get; init; }

    /// <summary>The project's TVA rate (0.11 is 11%). TVA is never revenue, so it changes what the client pays and owes, not profit.</summary>
    public decimal? TvaRate { get; init; }

    /// <summary>Completing with what the client still owes excused or written off: the price lowered to what they paid.</summary>
    public OwedSettlement? SettleOwed { get; init; }

    /// <summary>Finishing this many weeks later at the project's weekly cost of its last four full weeks.</summary>
    public int? DelayWeeks { get; init; }
}

/// <summary>Which months of a domain's spending to show.</summary>
public sealed record ExpenseTrendsQuery
{
    /// <summary>How many months, ending with this one: 1 to 24, 12 when left out.</summary>
    public int? Months { get; init; }
}
