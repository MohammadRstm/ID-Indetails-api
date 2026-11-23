using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A project's cost budget (the owner's decision Q8): what it may cost, line by line, in USD net of TVA, kept in
/// versions, each with its reason.
/// <list type="bullet">
/// <item>Design lines: a department of the project (its labour) or <see cref="Expenses"/>.</item>
/// <item>Execution lines: one of Execution's expense categories (Settings, so a new one has a line too) or
/// <see cref="GroundLabour"/>. Sites have no departments.</item>
/// <item>A line's cost is the shared calculator's cost on what it holds, all time; left = amount − cost (below zero
/// when over). Cost no line holds (labour in no department of the budget, a category with no line) is uncovered.</item>
/// </list>
/// </summary>
public static class BudgetRules
{
    public const int MaxReasonLength = 500;
    public const int MaxCategoryLength = 40;
    public const int MaxLines = 50;
    public const decimal MaxAmount = 100_000_000m;

    public const string Expenses = "expenses";
    public const string GroundLabour = "ground_labour";

    private static readonly IReadOnlyList<string> DesignCategories = [Expenses];

    /// <param name="TeamId">A Design department; null for a category line.</param>
    /// <param name="Category">One of <see cref="CategoriesOf"/>; null for a department line.</param>
    public sealed record Line(long? TeamId, string? Category, decimal Amount);

    /// <summary>A cost so far, keyed as a budget line would hold it; both keys null for cost no line can hold.</summary>
    public sealed record Cost(long? TeamId, string? Category, decimal Usd);

    public sealed record LineFigures(long? TeamId, string? Category, decimal Amount, decimal Cost)
    {
        public decimal Left => Amount - Cost;
    }

    /// <param name="Lines">In the order the lines were given.</param>
    /// <param name="Uncovered">Cost on things with no line.</param>
    public sealed record Figures(IReadOnlyList<LineFigures> Lines, decimal Uncovered);

    /// <summary>The categories a budget line of the business can have, in the order they are shown.</summary>
    /// <param name="siteCategories">Execution's expense categories' codes, in their order.</param>
    public static IReadOnlyList<string> CategoriesOf(string domain, IReadOnlyList<string> siteCategories)
        => domain == DomainCodes.Execution ? [.. siteCategories, GroundLabour] : DesignCategories;

    /// <summary>A category line's name in words: the expense category's own name where there is one.</summary>
    public static string NameOf(string category, IReadOnlyDictionary<string, string>? names = null) => category switch
    {
        Expenses => "Expenses",
        GroundLabour => "Ground labour",
        _ when names is not null && names.TryGetValue(category, out var name) => name,
        _ => char.ToUpperInvariant(category[0]) + category[1..].Replace('_', ' '),
    };

    /// <summary>
    /// A new version's lines against the project: departments only on Design and only its own, categories of its
    /// business, no line twice, and a day that has come.
    /// </summary>
    /// <param name="departments">The project's departments (Design).</param>
    /// <param name="siteCategories">Execution's expense categories' codes.</param>
    public static RuleDenial? Check(
        string domain,
        IReadOnlyList<Line> lines,
        IReadOnlyCollection<long> departments,
        DateOnly effectiveFrom,
        DateOnly today,
        IReadOnlyList<string> siteCategories)
    {
        if (effectiveFrom > today)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "effectiveFrom", "A budget can't start on a day still to come.");
        }

        var categories = CategoriesOf(domain, siteCategories);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TeamId is { } teamId)
            {
                if (domain != DomainCodes.Design)
                {
                    return RuleDenial.Invalid(ErrorCodes.BudgetLineInvalid, $"lines[{i}].teamId",
                        "An Execution site has no departments: budget its expense categories and ground labour.");
                }

                if (!departments.Contains(teamId))
                {
                    return RuleDenial.Invalid(ErrorCodes.BudgetLineInvalid, $"lines[{i}].teamId",
                        "That department doesn't work on this project: add it to the project first.");
                }
            }
            else if (line.Category is null || !categories.Contains(line.Category))
            {
                return RuleDenial.Invalid(ErrorCodes.BudgetLineInvalid, $"lines[{i}].category",
                    domain == DomainCodes.Design
                        ? "A Design project's budget has its departments and expenses."
                        : $"A site's budget has these categories: {string.Join(", ", categories)}.");
            }
        }

        return lines.GroupBy(l => (l.TeamId, l.Category)).Any(g => g.Count() > 1)
            ? RuleDenial.Invalid(ErrorCodes.BudgetLineInvalid, "lines", "Each department or category is budgeted once.")
            : null;
    }

    /// <summary>The total of a version: its lines added up.</summary>
    public static decimal TotalOf(IEnumerable<Line> lines) => lines.Sum(l => l.Amount);

    /// <summary>Each line's cost so far and what is left, and the cost no line holds, rounded to the cent.</summary>
    public static Figures Compare(IReadOnlyList<Line> lines, IReadOnlyList<Cost> costs)
    {
        bool Holds(Line line, Cost cost)
            => (line.TeamId is { } team && cost.TeamId == team) || (line.Category is { } category && cost.Category == category);

        return new Figures(
            lines.Select(l => new LineFigures(l.TeamId, l.Category, l.Amount, Round(costs.Where(c => Holds(l, c)).Sum(c => c.Usd)))).ToList(),
            Round(costs.Where(c => !lines.Any(l => Holds(l, c))).Sum(c => c.Usd)));
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
