using System.Globalization;
using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
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
using ID_Indetails.api.Services.Common.Implementation;
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
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        await CheckDomainAsync(domain, cancellationToken);
        var (from, to) = Period(query);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);

        // revenue is the money received: it moves as soon as a payment is recorded, whatever the project's progress
        var received = ReceivedOn(projects, rollup);
        var revenue = PeriodMoney.Received(received, from, to);
        var agreed = await AgreedAsync(projects, cancellationToken);
        var cost = PeriodMoney.Costs(projects.SelectMany(p => rollup.Costs(p.Id)), from, to);
        var clientWork = projects.Where(p => p.BillingType == BillingType.Client).Select(p => rollup.Money(p.Id).Summary).ToList();
        var profit = revenue - PeriodMoney.Round(cost.Total);
        var labourMinutes = (await financeSources.WorkEntriesAsync(projects.Select(p => p.Id).ToList(), cancellationToken))
            .Where(e => clock.ToCompanyDate(e.StartedAt) is var day && day >= from && day <= to)
            .Sum(e => e.Minutes);

        return new FinanceSummaryResponse(
            domain,
            from,
            to,
            revenue,
            ExpectedRevenue.In(agreed, from, to),
            PeriodCost(cost),
            profit,
            PeriodMoney.Margin(profit, revenue),
            UnconvertedOf(PeriodMoney.Unconverted(projects.SelectMany(p => rollup.Unconverted(p.Id)), from, to)),
            projects.Sum(p => rollup.Money(p.Id).UnpricedLabourMinutes),
            clientWork.Count,
            projects.Count - clientWork.Count,
            clientWork.Sum(s => s.Revenue),
            clientWork.Sum(s => s.Profit),
            projects.Where(p => p.BillingType == BillingType.Internal).Sum(p => rollup.Money(p.Id).Summary.Cost),
            projects.Where(p => p.BillingType == BillingType.Client)
                .Sum(p => Math.Max(0, rollup.Money(p.Id).Summary.Revenue - rollup.Payments.Where(x => x.ProjectId == p.Id).Sum(x => x.NetUsd))),
            rollup.Position(rollup.Billing(projects)),
            labourMinutes,
            // the TVA clients paid in the period: collected for the state, never revenue
            PeriodMoney.Received(rollup.Payments.Where(p => projects.Any(x => x.Id == p.ProjectId)).Select(p => (p.ReceivedOn, p.TvaUsd)), from, to));
    }

    public async Task<IReadOnlyList<FinanceMonthResponse>> MonthlyAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        await CheckDomainAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var received = ReceivedOn(projects, rollup);
        var agreed = await AgreedAsync(projects, cancellationToken);
        var costs = projects.SelectMany(p => rollup.Costs(p.Id)).ToList();
        var unconverted = projects.SelectMany(p => rollup.Unconverted(p.Id)).ToList();

        return Months(query.Months)
            .Select(month =>
            {
                var end = month.AddMonths(1).AddDays(-1);
                var revenue = PeriodMoney.Received(received, month, end);
                var cost = PeriodMoney.Costs(costs, month, end);
                return new FinanceMonthResponse(
                    month, revenue, ExpectedRevenue.In(agreed, month, end), Rounded(cost.Labour), Rounded(cost.Expenses), Rounded(cost.Materials), Rounded(cost.GroundLabour),
                    Rounded(cost.Total), revenue - Rounded(cost.Total), UnconvertedOf(PeriodMoney.Unconverted(unconverted, month, end)));
            })
            .ToList();
    }

    /// <summary>
    /// What the client projects agreed, dated: payments and fees as they were set or changed, and Execution materials at their
    /// client price, converted at the rate of the day they were added, on the day they were first sent.
    /// </summary>
    private async Task<IReadOnlyList<ExpectedRevenue.Agreed>> AgreedAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        var client = projects.Where(p => p.BillingType == BillingType.Client).ToDictionary(p => p.Id);
        var ids = client.Keys.ToList();
        var agreed = (await summaries.AgreedChangesAsync(ids, cancellationToken))
            .Select(c => ExpectedRevenue.Change(clock.ToCompanyDate(c.OccurredAt), c.Amount, c.PrevAmount))
            .ToList();

        var sites = client.Values.Where(p => p.DomainCode == DomainCodes.Execution).Select(p => p.Id).ToList();
        if (sites.Count > 0)
        {
            var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
            foreach (var m in (await financeSources.MaterialsAsync(sites, cancellationToken)).Where(m => m.SentAt is not null))
            {
                var price = ExecutionPricing.ClientPrice(new ExecutionPricing.Material(m.Total, m.ClientPrice), client[m.ProjectId].MaterialMarkup ?? 0);
                if (rates.ToUsd(price, m.Currency, clock.ToCompanyDate(m.AddedAt)) is { } usd)
                {
                    agreed.Add(new ExpectedRevenue.Agreed(clock.ToCompanyDate(m.SentAt!.Value), usd));
                }
            }
        }

        return agreed;
    }

    /// <summary>The domain's live payments as dated USD amounts, for revenue.</summary>
    private static IReadOnlyList<(DateOnly Day, decimal Usd)> ReceivedOn(IReadOnlyCollection<Project> projects, MoneyRollup rollup)
    {
        var ids = projects.Select(p => p.Id).ToHashSet();
        return rollup.Payments.Where(p => ids.Contains(p.ProjectId)).Select(p => (p.ReceivedOn, p.NetUsd)).ToList();
    }

    public async Task<(string FileName, string Csv)> ExportProjectsAsync(
        string domain,
        FinancePeriodQuery query,
        CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        var projects = await ProjectsAsync(domain, cancellationToken);
        var period = await SummaryAsync(domain, query, cancellationToken);
        static string Pct(decimal? margin) => margin is { } m ? (m * 100).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

        var csv = new StringBuilder();
        Csv.Line(csv, "Project", "Client", "Status", "Billing", "Revenue", "Currency", "Hours", "Labour cost", "Expenses", "Materials",
            "Ground labour", "Total cost", "Profit", "Margin %");
        foreach (var p in projects.Projects)
        {
            Csv.Line(
                csv,
                p.Title,
                p.ClientName ?? "Internal",
                p.Status.ToString().ToLowerInvariant(),
                p.BillingType == BillingType.Client ? "Client" : "Internal",
                Csv.Money(p.Revenue),
                PaymentRules.BaseCurrency,
                p.Hours.ToString("0.00", CultureInfo.InvariantCulture),
                Csv.Money(p.Cost.Labour),
                Csv.Money(p.Cost.Expenses),
                Csv.Money(p.Cost.Materials),
                Csv.Money(p.Cost.GroundLabour),
                Csv.Money(p.Cost.Total),
                Csv.Money(p.Profit),
                p.MarginKnown ? Pct(p.Margin) : string.Empty);
        }

        csv.AppendLine();
        Csv.Line(csv, "Period", $"{period.From:yyyy-MM-dd} to {period.To:yyyy-MM-dd}");
        Csv.Line(csv, "Revenue received", Csv.Money(period.Revenue));
        Csv.Line(csv, "Total cost", Csv.Money(period.Cost.Total));
        Csv.Line(csv, "Net profit", Csv.Money(period.Profit));
        Csv.Line(csv, "Margin %", Pct(period.Margin));

        return ($"financials-{domain}-{period.From:yyyy-MM-dd}-to-{period.To:yyyy-MM-dd}.csv", csv.ToString());
    }

    public async Task<FinanceProjectsResponse> ProjectsAsync(string domain, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
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
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
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
                x.Client.ClientId!.Value, ClientName(x.Client)!, x.Billing.Revenue, x.Billing.Invoiced, x.Billing.Received,
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
            ProjectBilling.Aging(rollup.Invoices, today).Select(a => new AgingResponse(a.FromDays, a.ToDays, a.Amount, a.Count)).ToList(),
            monthly,
            byClient,
            rollup.Payments.GroupBy(p => p.Method).Select(g => new MethodTotalResponse(g.Key, g.Sum(p => p.AmountUsd)))
                .OrderByDescending(m => m.Amount).ToList(),
            open,
            rollup.UnconvertedInvoices());
    }

    public async Task<IReadOnlyList<FinanceClientResponse>> ClientsAsync(string domain, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
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
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
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
            .Select(g => (UserId: g.Key, Cost: pricer.Price(g), Minutes: g.Sum(w => w.Minutes)))
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
                    l.Cost.Unconverted.Select(u => new CurrencyTotalResponse(u.Currency, u.Amount)).ToList(),
                    l.Minutes))
                .ToList(),
            expenses
                .Where(x => x.Usd is not null)
                .GroupBy(x => x.Category)
                .Select(g => new CategoryCostResponse(g.Key, g.Sum(x => x.Usd!.Value)))
                .OrderByDescending(c => c.Usd)
                .ToList(),
            expenses.OrderByDescending(x => x.Usd ?? -1).Take(BiggestExpenses).ToList(),
            UnconvertedOf(PeriodMoney.Unconverted(projects.SelectMany(p => rollup.Unconverted(p.Id)), from, to)));
    }

    public async Task<IReadOnlyList<MoneyAttentionResponse>> AttentionAsync(string domain, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        await CheckDomainAsync(domain, cancellationToken);
        var projects = (await summaries.DomainProjectsAsync(domain, cancellationToken)).OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToList();
        var byId = projects.ToDictionary(p => p.Id);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var facts = projects
            .Select(p =>
            {
                var summary = rollup.Money(p.Id).Summary;
                var billing = rollup.Billing([p]);
                return new MoneyAttention.Project(
                    p.Id, p.Status, p.BillingType == BillingType.Client, summary.Revenue, summary.Cost, billing.Outstanding, billing.Overdue);
            })
            .ToList();

        return MoneyAttention.Of(facts, rollup.Invoices, rollup.Today, MoneyAttention.WarnsOnCost(domain))
            .Select(i => new MoneyAttentionResponse(
                i.Kind,
                i.ProjectId,
                byId[i.ProjectId].Title,
                ClientName(byId[i.ProjectId]),
                i.InvoiceId,
                i.InvoiceId is { } id ? rollup.InvoiceRow(id).Number : null,
                i.DaysOverdue,
                i.Amount,
                i.Revenue,
                i.UsedPct))
            .ToList();
    }

    public async Task<FinanceProjectPeriodsResponse> ProjectPeriodsAsync(
        string domain,
        IReadOnlyList<FinancePeriodQuery> periods,
        CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        await CheckDomainAsync(domain, cancellationToken);
        var ranges = periods.Select(Period).ToList();
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var rollup = await rollups.LoadAsync(projects, cancellationToken);
        var agreed = await AgreedAsync(projects, cancellationToken);
        var payments = rollup.Payments.ToLookup(p => p.ProjectId);

        // the same figures as SummaryAsync, per project: payments on the day they arrived, costs dated in the period
        FinanceProjectPeriodMoneyResponse MoneyOf(Project project, DateOnly from, DateOnly to)
        {
            var received = PeriodMoney.Received(payments[project.Id].Select(p => (p.ReceivedOn, p.NetUsd)), from, to);
            var cost = PeriodMoney.Costs(rollup.Costs(project.Id), from, to);
            return new FinanceProjectPeriodMoneyResponse(
                from, to, received, PeriodCost(cost), received - Rounded(cost.Total), PeriodMoney.Unconverted(rollup.Unconverted(project.Id), from, to).Count > 0);
        }

        var received = ReceivedOn(projects, rollup);
        var costs = projects.SelectMany(p => rollup.Costs(p.Id)).ToList();
        var unconverted = projects.SelectMany(p => rollup.Unconverted(p.Id)).ToList();
        return new FinanceProjectPeriodsResponse(
            domain,
            ranges
                .Select(r =>
                {
                    var revenue = PeriodMoney.Received(received, r.From, r.To);
                    var cost = PeriodMoney.Costs(costs, r.From, r.To);
                    var profit = revenue - Rounded(cost.Total);
                    return new FinanceDomainPeriodResponse(
                        r.From, r.To, revenue, ExpectedRevenue.In(agreed, r.From, r.To), PeriodCost(cost), profit, PeriodMoney.Margin(profit, revenue),
                        UnconvertedOf(PeriodMoney.Unconverted(unconverted, r.From, r.To)));
                })
                .ToList(),
            projects
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .Select(p => new FinanceProjectPeriodResponse(
                    p.Id, p.Title, p.Status, p.BillingType, ranges.Select(r => MoneyOf(p, r.From, r.To)).ToList()))
                .ToList());
    }

    /// <summary>Money is shown per domain; a domain the caller can't see is not found.</summary>
    private async Task CheckDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.IsBusiness(domain) || !actor.CanSee(domain))
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
        => UnconvertedOf(costs
            .GroupBy(u => (u.Source, Currency: u.Currency.ToUpperInvariant()))
            .Select(g => new ProjectFinance.UnconvertedCost(g.Key.Source, g.Key.Currency, g.Sum(u => u.Amount))));

    private static IReadOnlyList<UnconvertedCostResponse> UnconvertedOf(IEnumerable<ProjectFinance.UnconvertedCost> costs)
        => costs.Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount)).ToList();
}
