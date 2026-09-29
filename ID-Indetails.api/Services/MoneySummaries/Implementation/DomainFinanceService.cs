using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.MoneySummaries.Implementation;

/// <summary>
/// Every figure comes from the shared finance calculator and is in USD at the rate of its day; anything with no rate
/// is listed as unconverted. Company overhead is never charged to a domain.
/// </summary>
public sealed class DomainFinanceService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IMoneySummaryRepository summaries,
    IMoneyRollupService rollups,
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IWorkEntryRepository people,
    IClock clock) : IDomainFinanceService
{
    public const int LabourPeople = 10;
    public const int BiggestExpenses = 12;

    public async Task<FinanceSummaryResponse> SummaryAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var (from, to) = Period(query);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);

        var shares = Recognition.Months(from, to, clock.CompanyToday);
        var revenue = PeriodMoney.Round(projects.Sum(p =>
            PeriodMoney.Revenue(Recognition.ByMonth(rollup.Money(p.Id).Summary.RecognisedToDate, rollup.Costs(p.Id)), shares)));
        var cost = PeriodMoney.Costs(projects.SelectMany(p => rollup.Costs(p.Id)), from, to);
        var clientWork = projects.Where(p => p.BillingType == BillingType.Client).Select(p => rollup.Money(p.Id).Summary).ToList();
        var profit = revenue - PeriodMoney.Round(cost.Total);

        return new FinanceSummaryResponse(
            domain,
            from,
            to,
            revenue,
            PeriodCost(cost),
            profit,
            PeriodMoney.Margin(profit, revenue),
            Unconverted(projects.SelectMany(p => rollup.Money(p.Id).Costs.Unconverted)),
            projects.Sum(p => rollup.Money(p.Id).UnpricedLabourMinutes),
            clientWork.Count,
            projects.Count - clientWork.Count,
            clientWork.Sum(s => s.Revenue),
            clientWork.Sum(s => s.Profit),
            projects.Where(p => p.BillingType == BillingType.Internal).Sum(p => rollup.Money(p.Id).Summary.Cost),
            clientWork.Sum(s => s.Revenue - s.RecognisedToDate),
            rollup.Position(rollup.Billing(projects)));
    }

    public async Task<IReadOnlyList<FinanceMonthResponse>> MonthlyAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var byMonth = projects
            .Select(p => Recognition.ByMonth(rollup.Money(p.Id).Summary.RecognisedToDate, rollup.Costs(p.Id)))
            .ToList();
        var costs = projects.SelectMany(p => rollup.Costs(p.Id)).ToList();

        return Months(query.Months)
            .Select(month =>
            {
                var revenue = PeriodMoney.Round(byMonth.Sum(m => m.GetValueOrDefault(month)));
                var cost = PeriodMoney.Costs(costs, month, month.AddMonths(1).AddDays(-1));
                return new FinanceMonthResponse(
                    month, revenue, Rounded(cost.Labour), Rounded(cost.Expenses), Rounded(cost.Materials), Rounded(cost.GroundLabour),
                    Rounded(cost.Total), revenue - Rounded(cost.Total));
            })
            .ToList();
    }

    public async Task<FinanceProjectsResponse> ProjectsAsync(string domain, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);

        var rows = projects
            .Select(p =>
            {
                var money = rollup.Money(p.Id);
                var s = money.Summary;
                return new FinanceProjectResponse(
                    p.Id,
                    p.Title,
                    ClientName(p),
                    p.Status,
                    p.BillingType,
                    s.Revenue,
                    s.RecognisedToDate,
                    MoneyRollup.Hours(money.LabourMinutes),
                    MoneyRollup.CostOf(money.Costs, money.UnpricedLabourMinutes),
                    s.Profit,
                    s.Margin,
                    s.MarginKnown,
                    s.Projected is { } x ? new ProjectProjectionResponse(x.Cost, x.Profit, x.Margin) : null,
                    s.CostPerHour);
            })
            .OrderByDescending(r => r.Profit)
            .ToList();

        var client = rows.Where(r => r.BillingType == BillingType.Client).ToList();
        return new FinanceProjectsResponse(
            rows,
            new FinanceProjectTotalsResponse(
                client.Count,
                rows.Count - client.Count,
                client.Sum(r => r.Revenue),
                client.Sum(r => r.Cost.Total),
                client.Sum(r => r.Profit),
                rows.Where(r => r.BillingType == BillingType.Internal).Sum(r => r.Cost.Total),
                rows.Sum(r => r.Cost.Total),
                Unconverted(projects.SelectMany(p => rollup.Money(p.Id).Costs.Unconverted))));
    }

    public async Task<FinanceBillingResponse> BillingAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var byId = projects.ToDictionary(p => p.Id);
        var today = rollup.Today;

        var issued = rollup.Invoices.Where(i => i.IsIssued && !i.IsVoided).ToList();
        var monthly = Months(query.Months)
            .Select(month => new BillingMonthResponse(
                month,
                issued.Where(i => Recognition.MonthOf(clock.ToCompanyDate(rollup.InvoiceRow(i.Id).IssuedAt!.Value)) == month).Sum(i => i.Total),
                rollup.Payments.Where(p => Recognition.MonthOf(p.ReceivedOn) == month).Sum(p => p.AmountUsd)))
            .ToList();

        var byClient = projects
            .Where(p => p.ClientId is not null)
            .GroupBy(p => p.ClientId!.Value)
            .Select(g => (Client: g.First(), Billing: rollup.Billing(g)))
            .Where(x => x.Billing.Invoiced > 0 || x.Billing.Drafts > 0)
            .Select(x => new ClientBillingResponse(
                x.Client.ClientId!.Value, ClientName(x.Client)!, x.Billing.Value, x.Billing.Invoiced, x.Billing.Received,
                x.Billing.Outstanding, x.Billing.Overdue))
            .OrderByDescending(c => c.Outstanding)
            .ToList();

        var open = rollup.Invoices
            .Where(i => i.IsOpen)
            .OrderBy(i => i.DueOn)
            .Select(i => new OpenInvoiceResponse(
                i.Id,
                rollup.InvoiceRow(i.Id).Number,
                i.ProjectId,
                byId[i.ProjectId].Title,
                ClientName(byId[i.ProjectId]) ?? string.Empty,
                i.DueOn,
                i.Balance,
                i.IsOverdue(today),
                i.DueOn is { } due && due < today ? today.DayNumber - due.DayNumber : 0))
            .ToList();

        return new FinanceBillingResponse(
            rollup.Position(rollup.Billing(projects)),
            BillingPosition.Aging(rollup.Invoices, today).Select(a => new AgingResponse(a.FromDays, a.ToDays, a.Amount, a.Count)).ToList(),
            monthly,
            byClient,
            rollup.Payments.GroupBy(p => p.Method).Select(g => new MethodTotalResponse(g.Key, g.Sum(p => p.AmountUsd)))
                .OrderByDescending(m => m.Amount).ToList(),
            open,
            rollup.UnconvertedInvoices);
    }

    public async Task<IReadOnlyList<FinanceClientResponse>> ClientsAsync(string domain, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var clients = await summaries.DomainClientsAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var byClient = projects.Where(p => p.ClientId is not null).ToLookup(p => p.ClientId!.Value);

        return clients
            .Where(c => byClient[c.Id].Any())
            .Select(c => new FinanceClientResponse(
                c.Id, ClientRules.DisplayName(c.Name, c.Organisation), c.Label, c.ArchivedAt is not null, rollup.ClientMoney(byClient[c.Id].ToList())))
            .OrderByDescending(c => c.Money.Revenue)
            .ToList();
    }

    public async Task<FinanceCostsResponse> CostsAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var (from, to) = Period(query);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var ids = projects.Select(p => p.Id).ToList();
        var titles = projects.ToDictionary(p => p.Id, p => p.Title);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));

        var work = (await financeSources.WorkEntriesAsync(ids, cancellationToken))
            .Where(e => clock.ToCompanyDate(e.StartedAt) is var day && day >= from && day <= to)
            .Select(e => new WorkRow(0, e.UserId, e.ProjectId, e.StartedAt, e.StartedAt.AddMinutes(e.Minutes), e.Minutes, e.StartedAt))
            .ToList();
        var pricer = await LabourPricer.LoadAsync(financeSources, finance, work, clock, cancellationToken);
        var names = (await people.PeopleAsync(work.Select(w => w.UserId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var labour = work
            .GroupBy(w => w.UserId)
            .Select(g => (UserId: g.Key, Cost: pricer.Price(g)))
            .ToList();

        var expenses = (await summaries.ExpensesAsync(ids, from, to, cancellationToken))
            .Select(x => new ExpenseItemResponse(
                x.Id, x.ProjectId, titles[x.ProjectId], x.Label, x.Category, x.SpentOn, x.Amount, x.Currency, rates.ToUsd(x.Amount, x.Currency, x.SpentOn)))
            .ToList();

        return new FinanceCostsResponse(
            from,
            to,
            PeriodCost(PeriodMoney.Costs(projects.SelectMany(p => rollup.Costs(p.Id)), from, to)),
            labour
                .OrderByDescending(l => l.Cost.Usd)
                .Take(LabourPeople)
                .Select(l => new PersonCostResponse(
                    l.UserId,
                    names.GetValueOrDefault(l.UserId, string.Empty),
                    l.Cost.Usd,
                    l.Cost.Unconverted.Select(u => new CurrencyTotalResponse(u.Currency, u.Amount)).ToList()))
                .ToList(),
            expenses
                .Where(x => x.Usd is not null)
                .GroupBy(x => x.Category)
                .Select(g => new CategoryCostResponse(g.Key, g.Sum(x => x.Usd!.Value)))
                .OrderByDescending(c => c.Usd)
                .ToList(),
            expenses.OrderByDescending(x => x.Usd ?? -1).Take(BiggestExpenses).ToList(),
            expenses
                .Where(x => x.Usd is null)
                .Select(x => (x.Currency, x.Amount))
                .Concat(labour.SelectMany(l => l.Cost.Unconverted.Select(u => (u.Currency, u.Amount))))
                .GroupBy(x => x.Currency.ToUpperInvariant())
                .Select(g => new CurrencyTotalResponse(g.Key, g.Sum(x => x.Amount)))
                .ToList());
    }

    /// <summary>Money is shown per domain; a domain the caller can't see is not found.</summary>
    private async Task CheckDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.All.Contains(domain) || !actor.CanSee(domain))
        {
            throw new NotFoundException("Domain not found.");
        }
    }

    private (DateOnly From, DateOnly To) Period(FinancePeriodQuery query)
    {
        var today = clock.CompanyToday;
        var to = query.To is { } t && t < today ? t : today;
        var from = query.From ?? WorkStats.QuarterStartOf(to);
        return (from > to ? to : from, to);
    }

    /// <summary>The given number of months, oldest first, ending with this one.</summary>
    private IReadOnlyList<DateOnly> Months(int count)
    {
        var current = Recognition.MonthOf(clock.CompanyToday);
        return Enumerable.Range(0, count).Select(i => current.AddMonths(i - count + 1)).ToList();
    }

    private static string? ClientName(Project p) => p.Client is { } c ? ClientRules.DisplayName(c.Name, c.Organisation) : null;

    private static PeriodCostResponse PeriodCost(PeriodMoney.CostTotals c)
        => new(Rounded(c.Labour), Rounded(c.Expenses), Rounded(c.Materials), Rounded(c.GroundLabour), Rounded(c.Total));

    private static decimal Rounded(decimal amount) => PeriodMoney.Round(amount);

    private static IReadOnlyList<UnconvertedCostResponse> Unconverted(IEnumerable<ProjectFinance.UnconvertedCost> costs)
        => costs
            .GroupBy(u => (u.Source, Currency: u.Currency.ToUpperInvariant()))
            .Select(g => new UnconvertedCostResponse(g.Key.Source, g.Key.Currency, g.Sum(u => u.Amount)))
            .ToList();
}
