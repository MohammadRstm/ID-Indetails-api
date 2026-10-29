using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Expenses;

/// <summary>
/// <list type="bullet">
/// <item>Expenses are added and removed only while the project is open (not completed, archived or finalised).</item>
/// <item>Totals are kept per currency. Amounts in different currencies are never added together.</item>
/// </list>
/// </summary>
public static class ExpenseRules
{
    public const string DefaultCategory = "other";

    public static readonly IReadOnlyList<string> Categories = ["assets", "services", "contractor", "software", "travel", "hardware", "other"];

    public sealed record CurrencyTotal(string Currency, decimal Total);

    /// <summary>An Execution site's costs are its site expenses: project expenses are for the other domains.</summary>
    public static RuleDenial? CheckNotExecution(string domain)
        => domain == DomainCodes.Execution
            ? RuleDenial.BusinessRule(ErrorCodes.ExpenseExecutionUsesSiteExpenses, "An Execution site's expenses are recorded as site expenses.")
            : null;

    public static RuleDenial? CheckEditable(ProjectStatus status, bool revenueLocked)
        => status is ProjectStatus.Completed or ProjectStatus.Archived || revenueLocked
            ? RuleDenial.BusinessRule(ErrorCodes.ProjectClosed, "This project is finished. Reopen it to change its expenses.")
            : null;

    public static IReadOnlyList<CurrencyTotal> Totals(IEnumerable<(string Currency, decimal Amount)> expenses)
        => expenses
            .GroupBy(e => e.Currency)
            .Select(g => new CurrencyTotal(g.Key, g.Sum(e => e.Amount)))
            .OrderBy(t => t.Currency)
            .ToList();
}
