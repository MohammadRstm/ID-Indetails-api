using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>
/// Company totals from the shared finance calculator: revenue recognised and spread over the months of
/// each project's costs (<c>Business/Finance/Recognition</c>), costs on their day, and overhead charged by
/// calendar days and converted at the rate of its month. Super admins' salaries are company costs like overhead:
/// they log no hours, so no project carries their pay.
/// </summary>
public sealed class CompanyOverviewService(
    ICompanyFinanceRepository company,
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IOverheadRepository overheads,
    IPayrollRepository payroll,
    IPayrollInputs payrollInputs,
    IClock clock) : ICompanyOverviewService
{
    public const int MinYear = 2000;

    private sealed record Data(
        IReadOnlyList<Project> Projects,
        IReadOnlyDictionary<long, ProjectCostHistory> History,
        ExchangeRateTable Rates);

    private static readonly DateOnly EarliestDay = new(2000, 1, 1);

    public async Task<CompanyOverviewResponse> GetAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        var end = to is { } t && t < today ? t : today;
        var start = from is { } f && f > EarliestDay ? f : from is null ? Recognition.MonthOf(end) : EarliestDay;
        if (start > end)
        {
            start = end;
        }

        bool InPeriod(DateOnly day) => day >= start && day <= end;
        var shares = Recognition.Months(start, end);
        var data = await LoadAsync(cancellationToken);
        var employees = await company.EmployeesAsync(cancellationToken);
        var clients = await company.ClientsAsync(cancellationToken);
        var invoices = await company.IssuedInvoicesAsync(cancellationToken);
        var payments = await company.PaymentsAsync(cancellationToken);

        var domains = DomainCodes.All.Select(domain =>
        {
            var projects = data.Projects.Where(p => p.DomainCode == domain).ToList();
            var history = projects.Select(p => (Project: p, History: data.History[p.Id])).ToList();

            // revenue is the money received, counted on the day it arrived: live as payments are recorded
            var revenue = PeriodMoney.Received(payments.Where(p => p.Domain == domain).Select(p => (p.ReceivedOn, p.AmountUsd)), start, end);
            var cost = PeriodMoney.Round(PeriodMoney.Costs(history.SelectMany(h => h.History.Costs), start, end).Total);
            var active = history
                .Where(h => h.History.Costs.Any(c => InPeriod(c.Day)) || h.History.Work.Any(w => InPeriod(w.Day)))
                .Select(h => h.Project)
                .ToList();
            var work = history.SelectMany(h => h.History.Work).ToList();
            // An invoice is converted at the rate of its issue day; one with no rate there is listed, not counted as 0.
            var domainInvoices = invoices
                .Where(i => i.Domain == domain)
                .Select(i => (Invoice: i, Usd: data.Rates.ToUsd(i.Total, i.Currency, clock.ToCompanyDate(i.IssuedAt))))
                .ToList();
            var converted = domainInvoices.Where(x => x.Usd is not null).ToList();

            return new DomainOverviewResponse(
                domain,
                revenue,
                cost,
                revenue - cost,
                PeriodMoney.Margin(revenue - cost, revenue),
                active.Count,
                projects.Count(p => p.Status == ProjectStatus.Active),
                employees.GetValueOrDefault(domain),
                work.Where(w => InPeriod(w.Day)).Select(w => w.UserId).Distinct().Count(),
                work.Count > 0,
                active.Where(p => p.ClientId is not null).Select(p => p.ClientId).Distinct().Count(),
                clients.GetValueOrDefault(domain),
                PeriodMoney.Round(converted.Where(x => InPeriod(clock.ToCompanyDate(x.Invoice.IssuedAt))).Sum(x => x.Usd!.Value)),
                PeriodMoney.Round(payments.Where(p => p.Domain == domain && InPeriod(p.ReceivedOn)).Sum(p => p.AmountUsd)),
                PeriodMoney.Round(converted.Sum(x => InvoiceRules.Balance(x.Usd!.Value, x.Invoice.PaidUsd))),
                PeriodMoney.Unconverted(history.SelectMany(h => h.History.Unconverted), start, end)
                    .Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount))
                    .ToList(),
                UnconvertedInvoices(domainInvoices.Where(x => x.Usd is null).Select(x => x.Invoice)));
        }).ToList();

        var (overhead, unconverted) = await OverheadAsync(shares.Select(s => (s.Month, s.Calendar)), data.Rates, cancellationToken);
        var (salaries, unconvertedSalaries) = await SuperAdminSalariesAsync(shares, data.Rates, cancellationToken);
        var gross = domains.Sum(d => d.Profit);

        return new CompanyOverviewResponse(
            start,
            end,
            domains,
            domains.Sum(d => d.Revenue),
            domains.Sum(d => d.Cost),
            gross,
            overhead,
            unconverted,
            salaries,
            unconvertedSalaries,
            gross - overhead - salaries,
            domains.Sum(d => d.Invoiced),
            domains.Sum(d => d.Collected),
            domains.Sum(d => d.Outstanding),
            domains.Sum(d => d.ProjectsWithActivity),
            domains.Sum(d => d.ClientsServed),
            UnconvertedInvoices(invoices.Where(i => data.Rates.ToUsd(i.Total, i.Currency, clock.ToCompanyDate(i.IssuedAt)) is null)));
    }

    public Task<IReadOnlyList<CompanyMonthResponse>> MonthlyAsync(int months, CancellationToken cancellationToken = default)
    {
        var thisMonth = Recognition.MonthOf(clock.CompanyToday);
        return MonthsAsync(Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - months + 1)).ToList(), cancellationToken);
    }

    public Task<IReadOnlyList<CompanyMonthResponse>> YearAsync(int year, CancellationToken cancellationToken = default)
    {
        var thisMonth = Recognition.MonthOf(clock.CompanyToday);
        if (year > thisMonth.Year || year < MinYear)
        {
            throw new RequestValidationException(
                ErrorCodes.ValidationFailed, "year", $"Choose a year from {MinYear} to {thisMonth.Year}.");
        }

        var months = year == thisMonth.Year ? thisMonth.Month : 12;
        return MonthsAsync(Enumerable.Range(1, months).Select(m => new DateOnly(year, m, 1)).ToList(), cancellationToken);
    }

    private async Task<IReadOnlyList<CompanyMonthResponse>> MonthsAsync(IReadOnlyList<DateOnly> keys, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);

        var received = (await company.PaymentsAsync(cancellationToken)).Select(p => (p.ReceivedOn, p.AmountUsd)).ToList();

        var costs = data.History.Values.SelectMany(h => h.Costs).ToList();

        var result = new List<CompanyMonthResponse>();
        foreach (var month in keys)
        {
            var (overhead, _) = await OverheadAsync([(month, 1m)], data.Rates, cancellationToken);
            var cost = PeriodMoney.Costs(costs, month, month.AddMonths(1).AddDays(-1));
            var labour = PeriodMoney.Round(cost.Labour);
            var expenses = PeriodMoney.Round(cost.Expenses);
            var materials = PeriodMoney.Round(cost.Materials);
            var ground = PeriodMoney.Round(cost.GroundLabour);
            var monthRevenue = PeriodMoney.Received(received, month, month.AddMonths(1).AddDays(-1));
            var before = monthRevenue - labour - expenses - materials - ground;

            result.Add(new CompanyMonthResponse(month, monthRevenue, labour, expenses, materials, ground, overhead, before, before - overhead));
        }

        return result;
    }

    private async Task<Data> LoadAsync(CancellationToken cancellationToken)
    {
        var projects = await company.ProjectsAsync(cancellationToken);
        return new Data(
            projects,
            await finance.CostHistoryAsync(projects, cancellationToken),
            new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken)));
    }

    /// <summary>Overhead of each month times its share, converted at the rate on the first of the month.</summary>
    private async Task<(decimal Usd, IReadOnlyList<CurrencyTotalResponse> Unconverted)> OverheadAsync(
        IEnumerable<(DateOnly Month, decimal Share)> months,
        ExchangeRateTable rates,
        CancellationToken cancellationToken)
    {
        var lines = await overheads.ListAsync(cancellationToken);
        var usd = 0m;
        var unconverted = new Dictionary<string, decimal>();

        foreach (var (month, share) in months)
        {
            foreach (var line in lines)
            {
                var amount = OverheadRules.AmountFor(
                    month, line.StartedOn, line.EndedOn, line.Amounts.Select(a => new OverheadRules.Amount(a.EffectiveFrom, a.Amount)));
                if (amount is null)
                {
                    continue;
                }

                if (rates.ToUsd(amount.Value, line.Currency, month) is { } converted)
                {
                    usd += converted * share;
                }
                else
                {
                    unconverted[line.Currency] = unconverted.GetValueOrDefault(line.Currency) + amount.Value * share;
                }
            }
        }

        return (PeriodMoney.Round(usd), unconverted.Select(u => new CurrencyTotalResponse(u.Key, PeriodMoney.Round(u.Value))).ToList());
    }

    /// <summary>
    /// The super admins' pay over the period, as Salaries works it out for each month it touches, times the share of the
    /// month's calendar days the period covers (like overhead), converted at the rate on the first of the month.
    /// </summary>
    private async Task<(decimal Usd, IReadOnlyList<CurrencyTotalResponse> Unconverted)> SuperAdminSalariesAsync(
        IReadOnlyList<Recognition.MonthShare> shares,
        ExchangeRateTable rates,
        CancellationToken cancellationToken)
    {
        var people = await payroll.PeopleAsync([], includeSuperAdmins: true, domain: null, search: null, shares[0].Month, cancellationToken);
        var usd = 0m;
        var unconverted = new Dictionary<string, decimal>();

        foreach (var month in await payrollInputs.ForAsync(people, shares.Select(s => s.Month).ToList(), cancellationToken))
        {
            if (SalaryCalculator.Calculate(month.Input) is not { } pay)
            {
                continue;
            }

            var amount = pay.Total * shares.Single(s => s.Month == month.Input.Month).Calendar;
            if (rates.ToUsd(amount, pay.Terms.Currency, month.Input.Month) is { } converted)
            {
                usd += converted;
            }
            else
            {
                unconverted[pay.Terms.Currency] = unconverted.GetValueOrDefault(pay.Terms.Currency) + amount;
            }
        }

        return (PeriodMoney.Round(usd), unconverted.Select(u => new CurrencyTotalResponse(u.Key, PeriodMoney.Round(u.Value))).ToList());
    }

    private static IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices(IEnumerable<IssuedInvoiceRow> invoices)
        => invoices
            .GroupBy(i => i.Currency.ToUpperInvariant())
            .Select(g => new UnconvertedInvoicesResponse(g.Key, g.Sum(i => i.Total), g.Count()))
            .ToList();
}
