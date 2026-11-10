using System.Globalization;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Insights.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Implementation;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Insights.Implementation;

/// <summary>
/// One project's profitability and its simulations, from the shared calculator (<see cref="IProjectFinanceService"/>) and
/// the pure rules in <c>Business/Finance/Intelligence</c>. Department labour comes from the departments service, without
/// its people: no figure here sits beside a person.
/// </summary>
public sealed class FinanceInsightService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectFinanceService finance,
    IProjectDepartmentService departments,
    IDesignVoRepository designVos,
    IBoqRepository boqs,
    IClock clock) : IFinanceInsightService
{
    /// <summary>The full weeks a delay is priced at.</summary>
    public const int RunRateWeeks = 4;

    private const string Method =
        "Agreed revenue − all cost so far + pocket money, in USD net of TVA, all time. Overhead, fixed " +
        "salaries, paid leave and the overtime premium are company costs, never a project's.";

    public async Task<ProjectInsightResponse> ProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        var money = await finance.GetAsync(project, cancellationToken);
        var summary = money.Summary;
        var client = project.BillingType == BillingType.Client;
        var changes = await PriceChangesAsync(project, money, cancellationToken);

        var bridge = ProjectBridgeRules.Of(new ProjectBridgeRules.Facts(
            project.DomainCode, client, summary.Revenue, changes.VariationOrders, changes.Settlement, changes.PaidAhead,
            changes.Supervision, project.PermitsPrice ?? 0, money.Costs, money.FromPocket - money.ToPocket));
        var departmentRows = project.DomainCode == DomainCodes.Design
            ? DepartmentsOf(await departments.GetAsync(project.Id, cancellationToken), money.Costs.Labour)
            : null;

        return new ProjectInsightResponse(
            project.Id,
            project.Title,
            project.DomainCode,
            project.Status,
            project.BillingType,
            summary.Revenue,
            client ? money.RevenueWithTva : 0,
            MoneyRollup.CostOf(money.Costs, money.UnpricedLabourMinutes),
            summary.Profit,
            summary.Margin,
            summary.MarginKnown,
            StandingOf(project, summary.Revenue, summary.Profit),
            project.ProgressPct,
            summary.RecognisedToDate,
            summary.Projected is { } p ? new ProjectProjectionResponse(p.Cost, p.Profit, p.Margin) : null,
            money.Received,
            money.ReceivedTva,
            client ? Math.Max(0, money.RevenueWithTva - money.Received) : 0,
            money.CashProfit,
            money.ToPocket,
            money.FromPocket,
            project.DomainCode == DomainCodes.Execution ? project.SupervisionRate ?? 0 : null,
            project.TvaRate,
            bridge.Select(s => new BridgeStepResponse(s.Kind, s.Amount)).ToList(),
            departmentRows,
            new InsightBasisResponse(Method, SummaryAssumptions(summary), LeftOut(money, client)));
    }

    public async Task<ProjectScenarioResponse> ScenarioAsync(long projectId, ProjectScenarioQuery query, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        var money = await finance.GetAsync(project, cancellationToken);
        CheckLevers(project, query, money);

        decimal? weeklyCost = null;
        if (query.DelayWeeks is not null)
        {
            weeklyCost = await WeeklyCostAsync(project, cancellationToken);
        }

        var marginShown = money.Summary.MarginKnown;
        var outcome = ProjectScenarioRules.Run(
            new ProjectScenarioRules.Facts(
                project.DomainCode,
                project.BillingType,
                money.Summary.Revenue,
                project.ProjectFee ?? 0,
                project.PermitsPrice ?? 0,
                project.SupervisionRate ?? 0,
                project.TvaRate,
                money.UntaxedUsd,
                money.Summary.Cost,
                money.FromPocket - money.ToPocket,
                money.Received),
            new ProjectScenarioRules.Levers(
                query.VoAmount ?? 0,
                query.ExtraCost ?? 0,
                query.SupervisionRate,
                query.TvaRate,
                query.SettleOwed is not null,
                (query.DelayWeeks ?? 0) * (weeklyCost ?? 0)));

        if (outcome.SettlementImpossible)
        {
            throw new BusinessRuleException(
                ErrorCodes.OwedBeyondWork, "The client paid less than the permits alone, so no work price makes what they paid the price.");
        }

        return new ProjectScenarioResponse(
            project.Id,
            FiguresOf(outcome.Before),
            FiguresOf(outcome.After),
            outcome.After.Profit - outcome.Before.Profit,
            outcome.FloorWouldRefuse,
            weeklyCost,
            new InsightBasisResponse(
                "The changes applied to today's figures with the same pricing rules as the real changes (agreed revenue − cost + " +
                "pocket money, net of TVA). Nothing is saved.",
                ScenarioAssumptions(project, query, weeklyCost, marginShown),
                LeftOut(money, project.BillingType == BillingType.Client)));
    }

    private async Task<Project> VisibleProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode)
            ? project
            : throw new NotFoundException("Project not found.");
    }

    /// <summary>What changed a project's base price, net of TVA, and the supervision left to show on its own.</summary>
    private sealed record PriceChanges(decimal VariationOrders, decimal Settlement, decimal PaidAhead, decimal Supervision);

    /// <summary>
    /// The VOs (settlement and closing ones apart), what a settlement took off (negative) and what keeping an overpayment
    /// added. On a site those two are whole price events, so each carries its own supervision, and the supervision step is
    /// what is left: the supervision on the base price and the VOs. A Design project's settlement lowered its payment, so
    /// it is the net of what was settled; an overpayment kept on Design is in its payment and isn't told apart.
    /// </summary>
    private async Task<PriceChanges> PriceChangesAsync(Project project, ProjectFinancials money, CancellationToken cancellationToken)
    {
        if (project.BillingType != BillingType.Client)
        {
            return new PriceChanges(0, 0, 0, 0);
        }

        if (project.DomainCode == DomainCodes.Design)
        {
            var settled = project.SettledOwedUsd is > 0 ? -Tva.NetFor(project.SettledOwedUsd.Value, project.TvaRate) : 0;
            return new PriceChanges((await designVos.AppliedAsync(project.Id, cancellationToken)).Total, settled, 0, 0);
        }

        var agreed = (await boqs.ListAsync(project.Id, cancellationToken))
            .Select(r => r.Boq)
            .Where(b => b.Status == BoqStatus.Agreed)
            .ToList();
        bool IsSettlement(Boq b) => b.Kind == BoqKind.Vo && b.Title is BoqRules.ExcusedTitle or BoqRules.WrittenOffTitle;
        // a site with no agreed BOQ keeps an overpayment as a client BOQ of that title
        bool IsPaidAhead(Boq b) => b.Title == BoqRules.ClosingTitle;
        var rate = project.SupervisionRate ?? 0;
        decimal SupervisionOn(decimal work) => Math.Round(work * rate, 2, MidpointRounding.AwayFromZero);

        var settlement = agreed.Where(IsSettlement).Sum(b => b.TotalUsd);
        var paidAhead = agreed.Where(IsPaidAhead).Sum(b => b.TotalUsd);
        return new PriceChanges(
            agreed.Where(b => b.Kind == BoqKind.Vo && !IsSettlement(b) && !IsPaidAhead(b)).Sum(b => b.TotalUsd),
            settlement + SupervisionOn(settlement),
            paidAhead + SupervisionOn(paidAhead),
            (money.Supervision ?? 0) - SupervisionOn(settlement) - SupervisionOn(paidAhead));
    }

    private static IReadOnlyList<DepartmentInsightResponse> DepartmentsOf(ProjectDepartmentsResponse response, decimal labour)
    {
        decimal? ShareOfLabour(decimal cost) => labour > 0 ? Math.Round(cost / labour, 4, MidpointRounding.AwayFromZero) : null;
        DepartmentInsightResponse Row(long? id, string name, decimal? share, DepartmentMoneyResponse money) => new(
            id, name, share, money.Budget, money.Cost, money.Left, money.Minutes, ShareOfLabour(money.Cost), money.Unconverted);

        var rows = response.Departments
            .Where(d => d.Money is not null)
            .Select(d => Row(d.Id, d.Name, d.Share, d.Money!))
            .ToList();
        if (response.Other is { } other)
        {
            rows.Add(Row(null, "Other hours", null, other));
        }

        return rows;
    }

    /// <summary>The refusals a real change would get, said before any figure is worked out.</summary>
    private static void CheckLevers(Project project, ProjectScenarioQuery query, ProjectFinancials money)
    {
        var site = project.DomainCode == DomainCodes.Execution;
        if (project.BillingType != BillingType.Client && (query.VoAmount is not null || query.TvaRate is not null || query.SettleOwed is not null || query.SupervisionRate is not null))
        {
            throw new BusinessRuleException(ErrorCodes.ScenarioNotApplicable, "Internal work has no price: only an extra cost or a delay can be simulated.");
        }

        if (query.SupervisionRate is not null && !site)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution sites have a supervision rate.");
        }

        if (query.ExtraCostSource is { } source)
        {
            var allowed = site ? new[] { CostSource.SiteExpense, CostSource.GroundLabour } : new[] { CostSource.Labour, CostSource.Expenses };
            if (!allowed.Contains(source))
            {
                throw new BusinessRuleException(ErrorCodes.NotForThisDomain,
                    site ? "A site's costs are site expenses or ground labour." : "A Design project's costs are labour or expenses.");
            }
        }

        if (query.SettleOwed is not null && money.RevenueWithTva - money.Received <= 0)
        {
            throw new BusinessRuleException(ErrorCodes.NothingOwed, "The client owes nothing on this project.");
        }
    }

    /// <summary>The average cost of the last full weeks (Monday to Sunday) before this one; 0 when there is none.</summary>
    private async Task<decimal> WeeklyCostAsync(Project project, CancellationToken cancellationToken)
    {
        var thisWeek = clock.CompanyToday.AddDays(-(((int)clock.CompanyToday.DayOfWeek + 6) % 7));
        var full = (await finance.CashByWeekAsync(project, clock.CompanyToday, DayOfWeek.Monday, cancellationToken))
            .Where(w => w.WeekStart < thisWeek)
            .TakeLast(RunRateWeeks)
            .ToList();
        return full.Count == 0 ? 0 : PeriodMoney.Round(full.Average(w => w.SpentInWeek));
    }

    private static ProfitStanding? StandingOf(Project project, decimal revenue, decimal profit)
        => ProfitFloorRules.Applies(project.BillingType, project.DomainCode, revenue, project.ProjectFee)
            ? StandingOf(ProfitFloorRules.StandingOf(profit, revenue))
            : null;

    private static ProfitStanding? StandingOf(ProfitFloorRules.Standing? standing) => standing switch
    {
        ProfitFloorRules.Standing.Fine => ProfitStanding.Fine,
        ProfitFloorRules.Standing.Close => ProfitStanding.Close,
        ProfitFloorRules.Standing.AtZero => ProfitStanding.AtZero,
        _ => null,
    };

    private static ScenarioFiguresResponse FiguresOf(ProjectScenarioRules.Figures f)
        => new(f.Revenue, f.RevenueWithTva, f.Cost, f.Profit, f.Margin, f.Owed, StandingOf(f.Standing));

    private static IReadOnlyList<string> SummaryAssumptions(ProjectFinance.Summary summary)
    {
        var assumptions = new List<string>
        {
            "The margin shows once the project is done, at least 5% along, or has 10 hours logged.",
        };
        if (summary.Projected is not null)
        {
            assumptions.Add("The projection divides the cost so far by the progress (requirements done): a straight line, an estimate.");
        }

        return assumptions;
    }

    private static List<string> ScenarioAssumptions(Project project, ProjectScenarioQuery query, decimal? weeklyCost, bool marginShown)
    {
        static string Usd(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture) + " USD";
        static string Pct(decimal rate) => (rate * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        var site = project.DomainCode == DomainCodes.Execution;
        var said = new List<string>();
        if (query.VoAmount is { } vo)
        {
            said.Add($"A variation order of {Usd(vo)} on the {(site ? "work price, with its supervision and TVA" : "price, with its TVA")}.");
        }

        if (query.ExtraCost is { } extra)
        {
            var where = query.ExtraCostSource switch
            {
                CostSource.Labour => "labour",
                CostSource.Expenses => "expenses",
                CostSource.SiteExpense => "site expenses",
                _ => "ground labour",
            };
            said.Add($"An extra cost of {Usd(extra)} in {where}.");
        }

        if (query.SupervisionRate is { } supervision)
        {
            said.Add($"Supervision at {Pct(supervision)} of the work price instead of {Pct(project.SupervisionRate ?? 0)}.");
        }

        if (query.TvaRate is { } tva)
        {
            said.Add($"TVA at {Pct(tva)} instead of {Pct(project.TvaRate)}: TVA is never revenue, so it changes what the client pays and owes, not profit.");
        }

        if (query.SettleOwed is { } settlement)
        {
            said.Add($"What the client still owes {(settlement == OwedSettlement.Excused ? "excused" : "written off")}: the price lowered to what they paid. A settlement isn't held to the profit floor.");
        }

        if (query.DelayWeeks is { } weeks)
        {
            said.Add(weeklyCost is > 0
                ? $"{weeks} more week(s) at {Usd(weeklyCost.Value)} a week, the average of the last {RunRateWeeks} full weeks."
                : $"{weeks} more week(s), but the project has no cost in its last full weeks, so the delay adds nothing.");
        }

        if (project.RevenueLockedAt is not null)
        {
            said.Add("The project is finished, so its price is locked: the changes show what they would have done.");
        }

        if (!marginShown)
        {
            said.Add("The margins are worked out for the comparison, though the project's own margin isn't shown yet (it shows once the project is done, 5% along or has 10 hours logged).");
        }

        return said;
    }

    private static IReadOnlyList<string> LeftOut(ProjectFinancials money, bool client)
    {
        var left = new List<string>();
        if (money.Costs.Unconverted.Count > 0)
        {
            left.Add("Costs with no exchange rate on their day, listed apart and not counted: " +
                string.Join(", ", money.Costs.Unconverted.Select(u => $"{u.Amount.ToString("#,0.##", CultureInfo.InvariantCulture)} {u.Currency} of {u.Source}")) + ".");
        }

        if (money.UnpricedLabourMinutes > 0)
        {
            left.Add($"{MoneyRollup.Hours(money.UnpricedLabourMinutes).ToString("0.##", CultureInfo.InvariantCulture)} hours logged by people with no pay record, not priced.");
        }

        if (!client)
        {
            left.Add("Internal work: no revenue, so no margin and no profit floor.");
        }

        return left;
    }
}
