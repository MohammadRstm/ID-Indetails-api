using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Insights.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Validators.Insights;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Insights.Implementation;

/// <summary>
/// A domain's project risks and spending trends. Figures come from the shared calculator (<see cref="IProjectFinanceService"/>),
/// a settlement's effect from a project's own bridge (<see cref="IFinanceInsightService"/>), a site's runway from the site
/// money timeline's projection (<see cref="LabourRules.ProjectZero"/>) and departments from the departments service,
/// without their people. The judgements are the pure rules in <c>Business/Finance/Intelligence</c>, with the owner's
/// thresholds.
/// </summary>
public sealed class DomainInsightService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IMoneySummaryRepository summaries,
    IProjectFinanceRepository financeSources,
    IProjectFinanceService finance,
    IBlockedChangeRepository blockedChanges,
    IProjectDepartmentService departments,
    ILabourRepository labour,
    IFinanceInsightService projectInsights,
    IClock clock,
    IOptions<ProtonOptions> options) : IDomainInsightService
{
    private ProtonInsightOptions Settings => options.Value.Insights;

    public async Task<ProjectRisksResponse> RisksAsync(string domain, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var today = clock.CompanyToday;
        var thresholds = new ProjectRiskRules.Thresholds(Settings.RunwayWorkingDays, Settings.SettlementMonths);
        var settledSince = today.AddMonths(-thresholds.SettlementMonths);
        // internal projects are cost centres (Q5): never a risk of loss
        var projects = (await summaries.DomainProjectsAsync(domain, cancellationToken))
            .Where(p => p.BillingType == BillingType.Client && (IsOpen(p) || SettledOn(p) >= settledSince))
            .ToList();
        var money = projects.Count == 0
            ? new Dictionary<long, ProjectFinancials>()
            : await finance.GetAsync(projects, cancellationToken);
        // a change kept until a department is added to the project isn't a money risk
        var pending = (await blockedChanges.PendingAsync(projects.Select(p => p.Id).ToList(), cancellationToken))
            .Where(c => c.Kind != BlockedChangeKind.WorkEntryDepartment)
            .ToLookup(c => c.ProjectId, c => clock.ToCompanyDate(c.RequestedAt));

        var found = new List<(long ProjectId, IReadOnlyList<ProjectRiskRules.Risk> Risks)>();
        var noAgreedAmount = 0;
        foreach (var project in projects)
        {
            var m = money[project.Id];
            var open = IsOpen(project);
            var floorApplies = ProfitFloorRules.Applies(project.BillingType, project.DomainCode, m.Summary.Revenue, project.ProjectFee);
            if (open && !floorApplies)
            {
                noAgreedAmount++;
            }

            var facts = new ProjectRiskRules.Facts(
                open,
                floorApplies,
                m.Summary.Revenue,
                m.Summary.Profit,
                m.Summary.Projected,
                project.ProgressPct,
                open && floorApplies && project.DomainCode == DomainCodes.Design ? await DepartmentsAsync(project, cancellationToken) : [],
                pending[project.Id].ToList(),
                m.Costs.Unconverted,
                open && project.DomainCode == DomainCodes.Execution ? await RunwayAsync(project, m, today, cancellationToken) : null,
                project.DomainCode == DomainCodes.Execution ? m.CashProfit : null,
                SettledOn(project) is { } settledOn && settledOn >= settledSince && m.Summary.Profit <= 0
                    ? await SettlementAsync(project, settledOn, cancellationToken)
                    : null);
            found.Add((project.Id, ProjectRiskRules.Of(facts, thresholds, today)));
        }

        var byId = projects.ToDictionary(p => p.Id);
        var groups = ProjectRiskRules.Rank(found)
            .Select(g => new ProjectRiskGroupResponse(
                g.ProjectId,
                byId[g.ProjectId].Title,
                byId[g.ProjectId].Status,
                g.Risks.Select(r => new ProjectRiskResponse(r.Kind, r.Amount is { } amount ? PeriodMoney.Round(amount) : null, r.Fact, r.Rule)).ToList()))
            .ToList();
        var counts = groups
            .SelectMany(g => g.Risks.Select(r => r.Kind).Distinct())
            .GroupBy(kind => kind)
            .OrderBy(g => g.Key)
            .Select(g => new RiskCountResponse(g.Key, g.Count()))
            .ToList();

        return new ProjectRisksResponse(domain, today, projects.Count, groups, counts, RiskBasis(domain, thresholds, noAgreedAmount));
    }

    public async Task<ExpenseTrendsResponse> ExpensesAsync(string domain, ExpenseTrendsQuery query, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var today = clock.CompanyToday;
        var count = query.Months ?? ExpenseTrendsQueryValidator.DefaultMonths;
        var current = Recognition.MonthOf(today);
        var window = Enumerable.Range(0, count).Select(i => current.AddMonths(i - count + 1)).ToList();
        var thresholds = new ExpenseTrendRules.Thresholds(
            Settings.ExpenseBaselineMonths, Settings.ExpenseHighRatio, Settings.ExpenseHighUsd,
            Settings.UnitCostJumpPct, Settings.UnitCostPurchases, Settings.DuplicateDays);

        // every project, internal ones too: their costs count in the domain's totals (Q5)
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var ids = projects.Select(p => p.Id).ToList();
        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
        var (expenses, categories) = domain == DomainCodes.Design
            ? DesignExpenses(await summaries.ExpensesAsync(ids, DateOnly.MinValue, today, cancellationToken), rates)
            : SiteExpenses(await summaries.SiteExpensesAsync(ids, DateOnly.MinValue, today, cancellationToken), rates);

        var months = ExpenseTrendRules.Months(expenses, categories, window, thresholds);
        var titles = projects.ToDictionary(p => p.Id, p => p.Title);
        var anomalies = ExpenseTrendRules.Anomalies(months, expenses, window[0], thresholds)
            .Select(a => new ExpenseAnomalyResponse(
                a.Kind, a.On, a.Category, a.ProjectId, a.ProjectId is { } id ? titles.GetValueOrDefault(id) : null,
                a.Item, a.Usd, a.Baseline, a.Pct, a.ExpenseIds, a.Fact, a.Rule))
            .ToList();
        var unconverted = expenses
            .Where(e => e.Usd is null && e.SpentOn >= window[0])
            .GroupBy(e => e.Currency.ToUpperInvariant())
            .OrderBy(g => g.Key)
            .Select(g => new CurrencyAmountResponse(g.Key, g.Sum(e => e.Amount)))
            .ToList();

        return new ExpenseTrendsResponse(
            domain,
            categories,
            months.Select(m => new ExpenseMonthResponse(
                m.Start, m.Total, m.Categories.Select(c => new ExpenseCategoryMonthResponse(c.Category, c.Usd, c.Baseline)).ToList())).ToList(),
            anomalies,
            unconverted,
            ExpenseBasis(domain, thresholds, expenses, window[0], unconverted));
    }

    /// <summary>Money is shown per domain; a domain the caller can't see is not found.</summary>
    private async Task CheckDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        if (!DomainCodes.IsBusiness(domain) || !actor.CanSee(domain))
        {
            throw new NotFoundException("Domain not found.");
        }
    }

    private static bool IsOpen(Project project) => project.Status is ProjectStatus.Active or ProjectStatus.Hold;

    /// <summary>The day a finished project was completed with what its client owed excused or written off; null otherwise.</summary>
    private DateOnly? SettledOn(Project project)
        => project is { Status: ProjectStatus.Completed or ProjectStatus.Archived, SettledOwedAs: not null, CompletedAt: { } completedAt }
            ? clock.ToCompanyDate(completedAt)
            : null;

    /// <summary>A Design project's departments with their budget and labour cost, never their people.</summary>
    private async Task<IReadOnlyList<ProjectRiskRules.Department>> DepartmentsAsync(Project project, CancellationToken cancellationToken)
        => (await departments.GetAsync(project.Id, cancellationToken)).Departments
            .Where(d => d.Money is not null)
            .Select(d => new ProjectRiskRules.Department(d.Name, d.Money!.Budget, d.Money!.Cost))
            .ToList();

    /// <summary>
    /// The site money timeline's projection of a running site, as <c>SiteMoneyService</c> makes it. A site on hold has no
    /// crew at work, so it gets no countdown: only spending past what the client paid shows.
    /// </summary>
    private async Task<LabourRules.Projection> RunwayAsync(Project project, ProjectFinancials money, DateOnly today, CancellationToken cancellationToken)
    {
        var defaults = project.Status == ProjectStatus.Active
            ? LabourRules.DefaultsAt(
                (await labour.ListDefaultsAsync(project.Id, cancellationToken))
                    .Select(r => new LabourRules.Defaults(r.Default.EffectiveFrom, r.Default.Workers, r.Default.DailyPay))
                    .ToList(),
                today)
            : null;
        return LabourRules.ProjectZero(false, money.CashProfit ?? 0, money.Summary.Profit, defaults, today);
    }

    /// <summary>What the settlement took off the profit: its step in the project's own bridge, so it is worked out once.</summary>
    private async Task<ProjectRiskRules.Settlement> SettlementAsync(Project project, DateOnly completedOn, CancellationToken cancellationToken)
    {
        var bridge = (await projectInsights.ProjectAsync(project.Id, cancellationToken)).Bridge;
        var taken = -bridge.Where(s => s.Kind == BridgeStepKind.Settlement).Sum(s => s.Amount);
        return new ProjectRiskRules.Settlement(project.SettledOwedAs!.Value, taken, completedOn);
    }

    private static (IReadOnlyList<ExpenseTrendRules.Expense> Expenses, IReadOnlyList<string> Categories) DesignExpenses(
        IReadOnlyList<ExpenseDetailRow> rows, ExchangeRateTable rates)
    {
        var expenses = rows
            .Select(r => new ExpenseTrendRules.Expense(
                r.Id, r.ProjectId, r.Label, r.Category, r.Currency, r.Amount, rates.ToUsd(r.Amount, r.Currency, r.SpentOn), r.SpentOn, null, null))
            .ToList();
        // the scheme's own order, then any older category the list no longer has
        var categories = ExpenseRules.Categories
            .Concat(expenses.Select(e => e.Category).Distinct().Where(c => !ExpenseRules.Categories.Contains(c)).Order(StringComparer.Ordinal))
            .ToList();
        return (expenses, categories);
    }

    private static (IReadOnlyList<ExpenseTrendRules.Expense> Expenses, IReadOnlyList<string> Categories) SiteExpenses(
        IReadOnlyList<SiteExpenseDetailRow> rows, ExchangeRateTable rates)
    {
        static string Code(SiteExpenseCategory category) => JsonNamingPolicy.SnakeCaseLower.ConvertName(category.ToString());
        var expenses = rows
            .Select(r =>
            {
                var usd = rates.ToUsd(r.Total, r.Currency, r.SpentOn);
                // entered as a quantity × a unit cost: the unit cost in USD, from the converted cost so cents don't round it away
                var unitCostUsd = r is { Quantity: > 0, UnitCost: > 0 } && usd is { } converted ? converted / r.Quantity.Value : (decimal?)null;
                return new ExpenseTrendRules.Expense(
                    r.Id, r.ProjectId, r.Name, Code(r.Category), r.Currency, r.Total, usd, r.SpentOn, unitCostUsd is null ? null : r.Unit, unitCostUsd);
            })
            .ToList();
        return (expenses, Enum.GetValues<SiteExpenseCategory>().Select(Code).ToList());
    }

    private static InsightBasisResponse RiskBasis(string domain, ProjectRiskRules.Thresholds t, int noAgreedAmount)
    {
        var assumptions = new List<string>
        {
            "The projection divides the cost so far by the progress (requirements done): a straight line, an estimate.",
        };
        if (domain == DomainCodes.Execution)
        {
            assumptions.Add("A site's runway counts working days (Monday to Saturday) from tomorrow at today's crew and day rate, against its " +
                "cash profit: received net of TVA − cost − moved to the pocket + paid by it.");
        }
        else
        {
            assumptions.Add("A department's budget is its share of the price plus its variation orders.");
        }

        var leftOut = new List<string> { "Internal projects: cost centres, never a risk of loss." };
        if (noAgreedAmount > 0)
        {
            leftOut.Add($"{noAgreedAmount} running project{(noAgreedAmount == 1 ? "" : "s")} with no agreed amount yet: no standing, projection or department budget to check.");
        }

        return new InsightBasisResponse(
            "Each active or on-hold client project of the domain, checked against the rules each risk names, with the shared " +
            $"calculator's figures (USD net of TVA, all time); a finished one only for a settlement in the last {t.SettlementMonths} months.",
            assumptions,
            leftOut);
    }

    private static InsightBasisResponse ExpenseBasis(
        string domain,
        ExpenseTrendRules.Thresholds t,
        IReadOnlyList<ExpenseTrendRules.Expense> expenses,
        DateOnly from,
        IReadOnlyList<CurrencyAmountResponse> unconverted)
    {
        var assumptions = new List<string>
        {
            ExpenseTrendRules.CategoryHighRule(t),
            domain == DomainCodes.Execution
                ? ExpenseTrendRules.UnitCostRule(t) + "; only site expenses entered as a quantity × a unit cost have one"
                : "Design expenses have no unit cost, so only categories and repeated entries are checked",
            ExpenseTrendRules.DuplicateRule(t),
            "This month runs to today.",
        };

        var leftOut = new List<string>();
        if (unconverted.Count > 0)
        {
            leftOut.Add("Spending with no exchange rate on its day, in no USD figure: " +
                string.Join(", ", unconverted.Select(u => $"{u.Amount.ToString("#,0.##", CultureInfo.InvariantCulture)} {u.Currency}")) + ".");
        }

        if (expenses.Count > 0 && from.AddMonths(-t.BaselineMonths) < Recognition.MonthOf(expenses.Min(e => e.SpentOn)))
        {
            leftOut.Add($"Months without {t.BaselineMonths} months of spending history before them have no average, so no category is called unusual in them.");
        }

        return new InsightBasisResponse(
            (domain == DomainCodes.Design ? "Project expenses" : "Site expenses") +
            " by category, month by month, in USD at the rate of each expense's day; internal projects included, as their " +
            "costs count in the domain's totals.",
            assumptions,
            leftOut);
    }
}
