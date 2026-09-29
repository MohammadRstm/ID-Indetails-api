using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using Cost = ID_Indetails.api.Business.Finance.ProjectFinance.CostItem;

namespace ID_Indetails.api.Services.Finance.Implementation;

public sealed class ProjectFinanceService(
    IProjectFinanceRepository sources,
    IPayrollRepository payroll,
    IScheduleRepository schedules,
    ICompanyRepository company,
    IClock clock) : IProjectFinanceService
{
    private sealed record Loaded(
        ExchangeRateTable Rates,
        ILookup<long, WorkEntryRow> Entries,
        IReadOnlyList<ProjectFinance.PayRate> PayRates,
        ILookup<long, Cost> Breaks,
        ILookup<long, ExpenseRow> Expenses,
        ILookup<long, MaterialRow> Materials,
        ILookup<long, GroundLabourRow> Ground,
        IReadOnlyDictionary<long, decimal> Received);

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
            var (items, _) = CostItems(entries, loaded.PayRates, loaded.Breaks[p.Id], loaded.Expenses[p.Id], loaded.Materials[p.Id], loaded.Ground[p.Id]);
            return new ProjectCostHistory(
                Calculate(p, loaded),
                ProjectFinance.DatedUsd(items, loaded.Rates),
                entries.Select(e => new WorkDay(e.UserId, clock.ToCompanyDate(e.StartedAt))).ToList());
        });
    }

    private async Task<Loaded> LoadAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var entries = await sources.WorkEntriesAsync(ids, cancellationToken);
        var payRates = await sources.PayRatesAsync(entries.Select(e => e.UserId).Distinct().ToList(), cancellationToken);

        return new Loaded(
            new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken)),
            entries.ToLookup(e => e.ProjectId),
            payRates,
            (await PaidBreaksAsync(entries, payRates, cancellationToken)).ToLookup(b => b.ProjectId, b => b.Cost),
            (await sources.ExpensesAsync(ids, cancellationToken)).ToLookup(x => x.ProjectId),
            (await sources.MaterialsAsync(ids, cancellationToken)).ToLookup(m => m.ProjectId),
            (await sources.GroundLabourAsync(ids, cancellationToken)).ToLookup(d => d.ProjectId),
            await sources.ReceivedAsync(ids, cancellationToken));
    }

    private ProjectFinancials Calculate(Project project, Loaded loaded)
        => Calculate(
            project,
            loaded.Entries[project.Id].ToList(),
            loaded.PayRates,
            loaded.Breaks[project.Id],
            loaded.Expenses[project.Id],
            loaded.Materials[project.Id].ToList(),
            loaded.Ground[project.Id],
            loaded.Received.GetValueOrDefault(project.Id),
            loaded.Rates);

    public async Task<IReadOnlyList<ProjectFinance.CashWeek>> CashByWeekAsync(Project project, DateOnly to, CancellationToken cancellationToken = default)
    {
        long[] ids = [project.Id];
        var rates = new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken));
        var entries = await sources.WorkEntriesAsync(ids, cancellationToken);
        var payRates = await sources.PayRatesAsync(entries.Select(e => e.UserId).Distinct().ToList(), cancellationToken);
        var breaks = (await PaidBreaksAsync(entries, payRates, cancellationToken)).Where(b => b.ProjectId == project.Id).Select(b => b.Cost);
        var (items, _) = CostItems(
            entries,
            payRates,
            breaks,
            await sources.ExpensesAsync(ids, cancellationToken),
            await sources.MaterialsAsync(ids, cancellationToken),
            await sources.GroundLabourAsync(ids, cancellationToken));
        var payments = await sources.PaymentsAsync(project.Id, cancellationToken);

        return ProjectFinance.CashByWeek(items, payments.Select(p => (p.ReceivedOn, p.AmountUsd)), rates, project.StartDate, to);
    }

    public async Task<IReadOnlyList<BreakCost>> PaidBreaksAsync(
        IReadOnlyCollection<WorkEntryRow> work,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        CancellationToken cancellationToken = default)
    {
        var hourly = payRates.Where(r => r.IsHourly).Select(r => r.UserId).ToHashSet();
        var theirs = work.Where(e => hourly.Contains(e.UserId)).ToList();
        if (theirs.Count == 0)
        {
            return [];
        }

        var userIds = theirs.Select(e => e.UserId).Distinct().ToList();
        var workedDays = theirs.Select(e => (e.UserId, Day: clock.ToCompanyDate(e.StartedAt))).ToHashSet();
        var allWork = (await payroll.WorkAsync(userIds, theirs.Min(e => e.StartedAt).AddDays(-1), theirs.Max(e => e.StartedAt).AddDays(1), cancellationToken))
            .ToLookup(e => e.UserId);
        var personal = (await schedules.SchedulesAsync(userIds, cancellationToken)).ToLookup(s => s.UserId);
        var policies = (await company.GetPoliciesAsync(cancellationToken))
            .Select(p => new PersonCalendar.Policy(p.EffectiveFrom, p.WorkDays, p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes, p.OvertimeMultiplier))
            .ToList();
        var rates = payRates.ToLookup(r => r.UserId);

        var costs = new List<BreakCost>();
        foreach (var userId in userIds)
        {
            var calendar = new PersonCalendar(policies, personal[userId].Select(s => new PersonCalendar.Schedule(
                s.EffectiveFrom, s.FollowsDefault, s.Days.ToDictionary(d => (int)d.Weekday, d => ((int)d.StartMinute, (int)d.EndMinute)))));
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
    /// Every cost of a project on its day, in its own currency; worked time is priced at each person's rate, and hourly
    /// staff's paid breaks are added as labour.
    /// </summary>
    private (IReadOnlyList<Cost> Items, int UnpricedMinutes) CostItems(
        IEnumerable<WorkEntryRow> entries,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        IEnumerable<Cost> breaks,
        IEnumerable<ExpenseRow> expenses,
        IEnumerable<MaterialRow> materials,
        IEnumerable<GroundLabourRow> ground)
    {
        var (labour, unpricedMinutes) = ProjectFinance.PriceLabour(
            entries.Select(e => new ProjectFinance.WorkedTime(e.UserId, clock.ToCompanyDate(e.StartedAt), e.Minutes)),
            payRates);

        var items = labour
            .Concat(breaks)
            .Concat(expenses.Select(x => new Cost(CostSource.Expenses, x.Currency, x.Amount, x.SpentOn)))
            .Concat(materials.Select(m => new Cost(CostSource.Materials, m.Currency, m.Total, clock.ToCompanyDate(m.AddedAt))))
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
        IReadOnlyList<MaterialRow> materials,
        IEnumerable<GroundLabourRow> ground,
        decimal received,
        ExchangeRateTable rates)
    {
        var (items, unpricedMinutes) = CostItems(entries, payRates, breaks, expenses, materials, ground);
        var costs = ProjectFinance.ToUsd(items, rates);

        var isExecution = project.DomainCode == DomainCodes.Execution;
        decimal? materialCharge = null;
        var unconvertedMaterials = 0;
        var revenue = project.Revenue;

        if (isExecution)
        {
            (var charge, unconvertedMaterials) = ProjectFinance.MaterialCharge(
                materials.Select(m => (new ExecutionPricing.Material(m.Total, m.ClientPrice), m.Currency, clock.ToCompanyDate(m.AddedAt))),
                project.MaterialMarkup ?? 0,
                rates);
            materialCharge = charge;

            // Open Execution projects are priced live; finalising freezes the agreed amount into Revenue.
            if (project.RevenueLockedAt is null)
            {
                revenue = charge + (project.ProjectFee ?? 0);
            }
        }

        var labourMinutes = entries.Sum(e => e.Minutes);
        var summary = ProjectFinance.Summarise(
            new ProjectFinance.Facts(
                project.BillingType == BillingType.Client,
                project.Status is ProjectStatus.Completed or ProjectStatus.Archived,
                project.ProgressPct,
                revenue,
                labourMinutes),
            costs);

        return new ProjectFinancials(
            summary,
            costs,
            labourMinutes,
            unpricedMinutes,
            materialCharge,
            unconvertedMaterials,
            received,
            isExecution ? received - costs.Total : null,
            isExecution ? materialCharge - costs.Materials : null);
    }
}
