namespace ID_Indetails.api.Models;

/// <summary>
/// One version of a project's cost budget: what it may cost, line by line, in USD net of TVA. Setting a budget again
/// adds the next version with its reason; earlier versions are kept as they were, never edited.
/// </summary>
public class ProjectBudget
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>1 for the first budget, one more at each revision.</summary>
    public int Version { get; set; }

    /// <summary>The company day it counts from: the day it was set, or an earlier one.</summary>
    public DateOnly EffectiveFrom { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long SetById { get; set; }

    public DateTime SetAt { get; set; }

    public List<ProjectBudgetLine> Lines { get; set; } = [];
}

/// <summary>
/// A line of a budget version: a Design department of the project (<see cref="TeamId"/>) or a category of cost
/// (<see cref="Category"/>: Design's "expenses", an Execution site's expense categories and "ground_labour"), never both.
/// </summary>
public class ProjectBudgetLine
{
    public long Id { get; set; }

    public long BudgetId { get; set; }

    public long? TeamId { get; set; }

    public string? Category { get; set; }

    public decimal AmountUsd { get; set; }
}
