using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>A line of a project's cost budget against what was spent on it so far.</summary>
/// <param name="DepartmentId">A Design department's line; null for a category.</param>
/// <param name="Category">expenses, a site expense category or ground_labour; null for a department.</param>
/// <param name="Over">The cost so far is above the line's amount.</param>
public sealed record BudgetLineRow(string Name, long? DepartmentId, string? Category, decimal Amount, decimal Cost, decimal Left, bool Over);

/// <summary>A version of the budget. It doesn't say who set it: no one is named beside an amount.</summary>
public sealed record BudgetVersionRow(int Version, DateOnly EffectiveFrom, string Reason, DateTime SetAt, decimal Total);

/// <summary>A project's cost budget, as GET /api/projects/{id}/budget returns it, without who set each version.</summary>
/// <param name="Current">The latest version; empty while the project has no budget.</param>
/// <param name="Uncovered">Cost so far on things with no line in the current version (all of it while there is none).</param>
/// <param name="Unconverted">Costs with no exchange rate on their day, in their own currency: in no figure here.</param>
/// <param name="Versions">Every version, the current one included, newest first.</param>
public sealed record ProjectCostBudget(
    long ProjectId,
    string Domain,
    BudgetVersionRow? Current,
    IReadOnlyList<BudgetLineRow> Lines,
    decimal Uncovered,
    IReadOnlyList<string>? Unconverted,
    IReadOnlyList<BudgetVersionRow> Versions,
    string Note);

/// <summary>
/// A project's cost budget: each line (a Design department or expenses; a site's expense categories and ground labour)
/// with its amount, the cost so far and what is left, the cost no line holds, and every version with its reason.
/// </summary>
public sealed class GetProjectBudgetTool(IProjectBudgetService budgets) : ProtonTool<ProjectIdInput>
{
    public override string Name => "get_project_budget";

    public override string Description =>
        "A project's cost budget, in USD net of TVA: the current version's lines (a Design project's departments and its " +
        "expenses; a site's expense categories, materials, contractor, equipment, fees and other, and its ground labour), " +
        "each with its amount, the cost so far, what is left and whether it is over; the cost no line holds; and every " +
        "version with its date and reason. This is the cost budget someone set, not a department's share of the price " +
        "(get_project_departments). Use it for \"what is its budget\", \"is it over budget\", \"how much is left\".";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectBudgetsController>(nameof(ProjectBudgetsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var b = await budgets.GetAsync(input.ProjectId, cancellationToken);
        var lines = b.Lines.Select(l => new BudgetLineRow(l.Name, l.TeamId, l.Category, l.Amount, l.Cost, l.Left, l.Left < 0)).ToList();
        var budget = new ProjectCostBudget(
            b.ProjectId,
            b.Domain,
            b.Current is null ? null : VersionOf(b.Current),
            lines,
            b.Uncovered,
            b.Unconverted.Count > 0 ? ProtonInsights.Amounts(b.Unconverted) : null,
            b.History.Select(VersionOf).ToList(),
            b.Current is null
                ? "The project has no cost budget yet: every cost so far is uncovered. Don't call anything over budget."
                : "All time, in USD net of TVA. A line's cost is what the project's money counts on it; left = amount − cost, " +
                  "below zero when it is over. Uncovered is cost no line holds. Never work out one person's cost from a department's line.");

        return new ProtonToolOutput(budget, [])
        {
            Basis = new ProtonBasis(
                $"Cost budget of project #{b.ProjectId}",
                null,
                null,
                false,
                null,
                $"project #{b.ProjectId}, all time",
                [
                    "Each line's cost is the project's cost so far on what it holds, from the shared calculator, net of TVA, " +
                    "converted at each day's rate: a department's labour, the project's expenses, a site's expenses of the category, its ground labour.",
                    "A line is over budget when its cost so far is above its amount.",
                ],
                b.Unconverted.Count > 0
                    ? new[] { $"Costs with no exchange rate on their day: {string.Join(", ", ProtonInsights.Amounts(b.Unconverted))}." }
                    : Array.Empty<string>()),
            Datasets = Datasets(b),
        };
    }

    private static BudgetVersionRow VersionOf(ProjectBudgetVersionResponse v) => new(v.Version, v.EffectiveFrom, v.Reason, v.SetAt, v.Total);

    private static IReadOnlyList<ProtonDataset> Datasets(ProjectBudgetResponse b)
    {
        if (b.Current is null)
        {
            return [];
        }

        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var lines = new ProtonDataset(
            $"Cost budget, version {b.Current.Version}",
            [ProtonDatasets.Label("line", "Line"), usd("amount", "Budget"), usd("cost", "Cost so far"), usd("left", "Left")],
            b.Lines.Select(l => ProtonDatasets.Row(null, ProtonCell.Of(l.Name), ProtonCell.Of(l.Amount), ProtonCell.Of(l.Cost), ProtonCell.Of(l.Left))).ToList(),
            "All time",
            Notes:
            [
                $"Net of TVA. Budgeted {ProtonInsights.Usd(b.Current.Total)} in all; cost no line holds: {ProtonInsights.Usd(b.Uncovered)}.",
            ]);
        return ProtonDatasets.WithRows(lines);
    }
}
