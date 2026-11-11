using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.DTO.Response.Projects;

/// <summary>
/// A project's cost budget: its current version line by line against the cost so far, all in USD net of TVA, and every
/// version there has been.
/// </summary>
/// <param name="Current">The latest version; null while the project has no budget.</param>
/// <param name="Lines">One per line of the current version: departments by name, then the categories in their order.</param>
/// <param name="Uncovered">Cost so far on things with no line in the current version (all the cost while there is none).</param>
/// <param name="Unconverted">Costs with no exchange rate on their day, in their own currency: in no figure above.</param>
/// <param name="History">Every version, the current one included, newest first.</param>
public sealed record ProjectBudgetResponse(
    long ProjectId,
    string Domain,
    ProjectBudgetVersionResponse? Current,
    IReadOnlyList<ProjectBudgetLineResponse> Lines,
    decimal Uncovered,
    IReadOnlyList<CurrencyAmountResponse> Unconverted,
    IReadOnlyList<ProjectBudgetVersionResponse> History);

/// <param name="Total">Its lines added up.</param>
public sealed record ProjectBudgetVersionResponse(
    int Version,
    DateOnly EffectiveFrom,
    string Reason,
    string SetByName,
    DateTime SetAt,
    decimal Total);

/// <param name="TeamId">A Design department; null for a category line.</param>
/// <param name="Category">expenses (Design); materials, contractor, equipment, fees, other or ground_labour (Execution); null for a department.</param>
/// <param name="Name">The department's name, or the category in words.</param>
/// <param name="Cost">What was spent on it so far, all time.</param>
/// <param name="Left">Amount − cost; below zero when it is over.</param>
public sealed record ProjectBudgetLineResponse(
    long? TeamId,
    string? Category,
    string Name,
    decimal Amount,
    decimal Cost,
    decimal Left);
