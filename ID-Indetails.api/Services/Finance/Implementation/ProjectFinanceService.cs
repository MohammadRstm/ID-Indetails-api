using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Cost = ID_Indetails.api.Business.Finance.ProjectFinance.CostItem;

namespace ID_Indetails.api.Services.Finance.Implementation;

public sealed class ProjectFinanceService(
    IProjectFinanceRepository sources,
    IPayrollRepository payroll,
    IPayrollInputs payrollInputs,
    IClock clock) : IProjectFinanceService
{
    private sealed record Loaded(
        ExchangeRateTable Rates,
        ILookup<long, WorkEntryRow> Entries,
        IReadOnlyList<ProjectFinance.PayRate> PayRates,
        ILookup<long, Cost> Breaks,
        ILookup<long, ExpenseRow> Expenses,
        ILookup<long, SiteExpenseRow> SiteExpenses,
        ILookup<long, GroundLabourRow> Ground,
        IReadOnlyDictionary<long, (decimal Received, decimal Tva)> Received,
        IReadOnlyDictionary<long, (decimal ToPocket, decimal FromPocket)> Pocket);

    public async Task<ProjectFinancials> GetAsync(Project project, CancellationToken cancellationToken = default)
        => (await GetAsync([project], cancellationToken))[project.Id];

    public async Task<IReadOnlyDictionary<long, ProjectFinancials>> GetAsync(
        IReadOnlyCollection<Project> projects,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(projects, cancellationToken);
        return projects.ToDictionary(p => p.Id, p => Calculate(p, loaded));
    }

    public async Task<IReadOnlyDictionary<long, ProjectCostHistory>> CostHistoryAsync(
        IReadOnlyCollection<Project> projects,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(projects, cancellationToken);
        return projects.ToDictionary(p => p.Id, p =>
        {
            var entries = loaded.Entries[p.Id].ToList();
            var (items, _) = CostItems(entries, loaded.PayRates, loaded.Breaks[p.Id], loaded.Expenses[p.Id], loaded.SiteExpenses[p.Id], loaded.Ground[p.Id]);
            return new ProjectCostHistory(
                Calculate(p, loaded),
                ProjectFinance.DatedUsd(items, loaded.Rates),
                ProjectFinance.Unconvertible(items, loaded.Rates),
                entries.Select(e => new WorkDay(e.UserId, clock.ToCompanyDate(e.StartedAt), e.Minutes)).ToList());
        });
    }

    private async Task<Loaded> LoadAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var entries = await sources.WorkEntriesAsync(ids, cancellationToken);
        var payRates = await PayRatesAsync(entries.Select(e => e.UserId).Distinct().ToList(), cancellationToken);

        return new Loaded(
            new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken)),
            entries.ToLookup(e => e.ProjectId),
            payRates,
            (await PaidBreaksAsync(entries, payRates, cancellationToken)).ToLookup(b => b.ProjectId, b => b.Cost),
            (await sources.ExpensesAsync(ids, cancellationToken)).ToLookup(x => x.ProjectId),
            (await sources.SiteExpensesAsync(ids, cancellationToken)).ToLookup(e => e.ProjectId),
            (await sources.GroundLabourAsync(ids, cancellationToken)).ToLookup(d => d.ProjectId),
            await sources.ReceivedAsync(ids, cancellationToken),
            await sources.PocketAsync(ids, cancellationToken));
    }

    private ProjectFinancials Calculate(Project project, Loaded loaded)
        => Calculate(
            project,
            loaded.Entries[project.Id].ToList(),
            loaded.PayRates,
            loaded.Breaks[project.Id],
            loaded.Expenses[project.Id],
            loaded.SiteExpenses[project.Id].ToList(),
            loaded.Ground[project.Id],
            loaded.Received.GetValueOrDefault(project.Id),
            loaded.Pocket.GetValueOrDefault(project.Id),
            loaded.Rates);

    public async Task<IReadOnlyList<ProjectFinance.CashWeek>> CashByWeekAsync(
        Project project,
        DateOnly to,
        DayOfWeek weekStart = DayOfWeek.Monday,
        CancellationToken cancellationToken = default)
    {
        long[] ids = [project.Id];
        var rates = new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken));
        var entries = await sources.WorkEntriesAsync(ids, cancellationToken);
        var payRates = await PayRatesAsync(entries.Select(e => e.UserId).Distinct().ToList(), cancellationToken);
        var breaks =(await PaidBreaksAsync(entries, payRates, cancellationToken)).Where(b => b.ProjectId == project.Id).Select(b => b.Cost);
        var (items, _) = CostItems(
            entries,
            payRates,
            breaks,
            await sources.ExpensesAsync(ids, cancellationToken),
            await sources.SiteExpensesAsync(ids, cancellationToken),
            await sources.GroundLabourAsync(ids, cancellationToken));
        var payments = await sources.PaymentsAsync(project.Id, cancellationToken);
        // pocket money counts as cash on the day it moved: a payout in, a contribution out
        var pocket = await sources.PocketMovesAsync(project.Id, cancellationToken);

        return ProjectFinance.CashByWeek(
            items,
            payments.Select(p => (p.ReceivedOn, p.AmountUsd)).Concat(pocket.Select(m => (clock.ToCompanyDate(m.At), m.AmountUsd))),
            entries.Select(e => (clock.ToCompanyDate(e.StartedAt), e.Minutes)),
            rates,
            project.StartDate,
            to,
            weekStart);
    }

    public async Task<IReadOnlyList<ProjectFinance.PayRate>> PayRatesAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var history = (await sources.PayHistoryAsync(userIds, cancellationToken)).ToLookup(p => p.UserId);
        if (history.Count == 0)
        {
            return [];
        }

        var ids = history.Select(g => g.Key).ToList();
        var calendars = await payrollInputs.CalendarsAsync(ids, cancellationToken);
        var roles = await payrollInputs.RoleHistoriesAsync(ids, cancellationToken);
        return history.SelectMany(g => ProjectFinance.RatesOf(g, calendars[g.Key], clock.CompanyToday, roles[g.Key])).ToList();
    }

    public async Task<IReadOnlyList<BreakCost>> PaidBreaksAsync(
        IReadOnlyCollection<WorkEntryRow> work,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        CancellationToken cancellationToken = default)
    {
        var priced = payRates.Select(r => r.UserId).ToHashSet();
        var theirs = work.Where(e => priced.Contains(e.UserId)).ToList();
        if (theirs.Count == 0)
        {
            return [];
        }

        var userIds = theirs.Select(e => e.UserId).Distinct().ToList();
        var workedDays = theirs.Select(e => (e.UserId, Day: clock.ToCompanyDate(e.StartedAt))).ToHashSet();
        var allWork = (await payroll.WorkAsync(userIds, theirs.Min(e => e.StartedAt).AddDays(-1), theirs.Max(e => e.StartedAt).AddDays(1), cancellationToken))
            .ToLookup(e => e.UserId);
        var calendars = await payrollInputs.CalendarsAsync(userIds, cancellationToken);
        var rates = payRates.ToLookup(r => r.UserId);

        var costs = new List<BreakCost>();
        foreach (var userId in userIds)
        {
            var calendar = calendars[userId];
            var days = allWork[userId]
                .Select(e => (Entry: e, Local: clock.ToCompanyTime(e.StartedAt)))
                .Select(x => new PaidBreaks.Work(x.Entry.Id, x.Entry.ProjectId, DateOnly.FromDateTime(x.Local), (int)x.Local.TimeOfDay.TotalMinutes, x.Entry.Minutes))
                .ToList();
            var shares = PaidBreaks.Allocate(days, calendar).Where(s => workedDays.Contains((userId, s.Day)));
            costs.AddRange(PaidBreaks.Price(shares, rates[userId].ToList()).Select(p => new BreakCost(userId, p.ProjectId, p.Cost)));
        }

        return costs;
    }

    /// <summary>
    /// Every cost of a project on its day, in its own currency; worked time is priced at each person's rate, and their
    /// paid breaks are added as labour.
    /// </summary>
    private (IReadOnlyList<Cost> Items, int UnpricedMinutes) CostItems(
        IEnumerable<WorkEntryRow> entries,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        IEnumerable<Cost> breaks,
        IEnumerable<ExpenseRow> expenses,
        IEnumerable<SiteExpenseRow> siteExpenses,
        IEnumerable<GroundLabourRow> ground)
    {
        var (labour, unpricedMinutes) = ProjectFinance.PriceLabour(
            entries.Select(e => new ProjectFinance.WorkedTime(e.UserId, clock.ToCompanyDate(e.StartedAt), e.Minutes)),
            payRates);

        var items = labour
            .Concat(breaks)
            .Concat(expenses.Select(x => new Cost(CostSource.Expenses, x.Currency, x.Amount, x.SpentOn)))
            .Concat(siteExpenses.Select(e => new Cost(CostSource.SiteExpense, e.Currency, e.Total, e.SpentOn)))
            // Ground labour is recorded without a currency: daily pay is in USD.
            .Concat(ground.Select(d => new Cost(CostSource.GroundLabour, PaymentRules.BaseCurrency, d.Cost, d.WorkDate)))
            .ToList();

        return (items, unpricedMinutes);
    }

    private ProjectFinancials Calculate(
        Project project,
        IReadOnlyList<WorkEntryRow> entries,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        IEnumerable<Cost> breaks,
        IEnumerable<ExpenseRow> expenses,
        IReadOnlyList<SiteExpenseRow> siteExpenses,
        IEnumerable<GroundLabourRow> ground,
        (decimal Gross, decimal Tva) received,
        (decimal ToPocket, decimal FromPocket) pocket,
        ExchangeRateTable rates)
    {
        var (items, unpricedMinutes) = CostItems(entries, payRates, breaks, expenses, siteExpenses, ground);
        var costs = ProjectFinance.ToUsd(items, rates);

        var isExecution = project.DomainCode == DomainCodes.Execution;
        var revenue = project.Revenue;
        var revenueWithTva = Tva.Gross(revenue, project.TvaRate);
        var revenueTva = revenueWithTva - revenue;
        decimal? supervision = null;

        if (isExecution)
        {
            // Open Execution projects are priced live from the agreed BOQs; finalising freezes the agreed amount into Revenue.
            var price = ExecutionPricing.PriceOf(project.ProjectFee ?? 0, project.SupervisionRate ?? 0, project.TvaRate);
            supervision = price.Supervision;
            revenueWithTva = price.Gross;
            revenueTva = price.Tva;
            if (project.RevenueLockedAt is null)
            {
                revenue = price.Net;
            }
        }

        var labourMinutes = entries.Sum(e => e.Minutes);
        var summary = ProjectFinance.Summarise(
            new ProjectFinance.Facts(
                project.BillingType == BillingType.Client,
                project.Status is ProjectStatus.Completed or ProjectStatus.Archived,
                project.ProgressPct,
                revenue,
                labourMinutes,
                pocket.FromPocket - pocket.ToPocket),
            costs);

        return new ProjectFinancials(
            summary,
            costs,
            labourMinutes,
            unpricedMinutes,
            received.Gross,
            isExecution ? received.Gross - received.Tva - costs.Total - pocket.ToPocket + pocket.FromPocket : null,
            pocket.ToPocket,
            pocket.FromPocket,
            received.Tva,
            supervision,
            revenueWithTva,
            revenueTva);
    }
}
