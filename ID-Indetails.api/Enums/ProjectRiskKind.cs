namespace ID_Indetails.api.Enums;

/// <summary>What a project's financial risk is about, most pressing first: the order is the ranking.</summary>
public enum ProjectRiskKind
{
    /// <summary>The agreed profit is at zero or below.</summary>
    ProfitAtZero,

    /// <summary>A running site whose cash profit is below zero, or runs out soon at today's crew and day rate.</summary>
    ShortRunway,

    /// <summary>The cost so far ÷ progress puts the profit at completion at zero or below.</summary>
    ProjectedLoss,

    /// <summary>A margin of 10% or less.</summary>
    ProfitClose,

    /// <summary>A change the profit floor refused, waiting to be forced or dismissed.</summary>
    BlockedChange,

    /// <summary>A Design department's labour cost past its budget.</summary>
    DepartmentOverBudget,

    /// <summary>Costs with no exchange rate on their day, left out of cost and profit.</summary>
    UnconvertedCosts,

    /// <summary>A finished project whose owed amount was excused or written off, ending at an agreed profit of zero or below.</summary>
    SettlementAtZero,
}
