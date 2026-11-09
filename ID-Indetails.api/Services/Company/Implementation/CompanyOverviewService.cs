using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
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
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>
/// Company totals from the shared finance calculator: revenue is the money received, on the day it arrived; costs
/// count on their day; overhead is charged by calendar days and converted at the rate of its month. Fixed salaries (the
/// pay of everyone who logs no hours: admins, super admins and Execution's site staff), Design salaries' paid leave and
/// the overtime premium past a salary's threshold are company costs like overhead: no project carries them. Every figure needs company:view, checked here as well as on
/// the endpoint.
/// </summary>
public sealed class CompanyOverviewService(
    ICompanyFinanceRepository company,
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IOverheadRepository overheads,
    IPayrollRepository payroll,
    IPayrollInputs payrollInputs,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IClock clock) : ICompanyOverviewService
{
    public const int MinYear = 2000;

    private sealed record Data(
        IReadOnlyList<Project> Projects,
        IReadOnlyDictionary<long, ProjectCostHistory> History,
        ExchangeRateTable Rates);

    /// <summary>One salary cost the company carries as a whole, added up by month in USD, with what had no rate for its month.</summary>
    private sealed class SalaryLine
    {
        private readonly Dictionary<DateOnly, decimal> _usd = [];
        private readonly Dictionary<string, decimal> _unconverted = [];

        public decimal Total => PeriodMoney.Round(_usd.Values.Sum());

        public decimal In(DateOnly month) => PeriodMoney.Round(_usd.GetValueOrDefault(month));

        public IReadOnlyList<CurrencyTotalResponse> Unconverted
            => _unconverted.Select(u => new CurrencyTotalResponse(u.Key, PeriodMoney.Round(u.Value))).ToList();

        /// <summary>Converted at the rate on the first of <paramref name="month"/>; listed apart when there is none.</summary>
        public void Add(decimal amount, string currency, DateOnly month, ExchangeRateTable rates)
        {
            if (amount == 0)
            {
                return;
            }

            if (rates.ToUsd(amount, currency, month) is { } converted)
            {
                _usd[month] = _usd.GetValueOrDefault(month) + converted;
            }
            else
            {
                _unconverted[currency] = _unconverted.GetValueOrDefault(currency) + amount;
            }
        }
    }

    /// <summary>The salary costs no project carries.</summary>
    private sealed record SalaryCosts(SalaryLine Fixed, SalaryLine PaidLeave, SalaryLine OvertimePremium);

    private static readonly DateOnly EarliestDay = new(2000, 1, 1);

    public async Task<CompanyOverviewResponse> GetAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.CompanyView);
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

        var domains = DomainCodes.Businesses.Select(domain =>
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
        var salaryCosts = await SalaryCostsAsync(shares, data.Rates, cancellationToken);
        var (salaries, paidLeave, premium) = (salaryCosts.Fixed.Total, salaryCosts.PaidLeave.Total, salaryCosts.OvertimePremium.Total);
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
            salaryCosts.Fixed.Unconverted,
            paidLeave,
            salaryCosts.PaidLeave.Unconverted,
            premium,
            salaryCosts.OvertimePremium.Unconverted,
            salaries,
            salaryCosts.Fixed.Unconverted,
            gross - overhead - salaries - paidLeave - premium,
            domains.Sum(d => d.Invoiced),
            domains.Sum(d => d.Collected),
            domains.Sum(d => d.Outstanding),
            domains.Sum(d => d.ProjectsWithActivity),
            domains.Sum(d => d.ClientsServed),
            UnconvertedInvoices(invoices.Where(i => data.Rates.ToUsd(i.Total, i.Currency, clock.ToCompanyDate(i.IssuedAt)) is null)));
    }

    public async Task<PeriodOverheadResponse> OverheadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.CompanyView);
        var end = to < clock.CompanyToday ? to : clock.CompanyToday;
        var start = from > end ? end : from;
        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
        var (usd, unconverted) = await OverheadAsync(Recognition.Months(start, end).Select(s => (s.Month, s.Calendar)), rates, cancellationToken);
        return new PeriodOverheadResponse(start, end, usd, unconverted);
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
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.CompanyView);
        var data = await LoadAsync(cancellationToken);

        var received = (await company.PaymentsAsync(cancellationToken)).Select(p => (p.ReceivedOn, p.AmountUsd)).ToList();

        var costs = data.History.Values.SelectMany(h => h.Costs).ToList();
        var salaryCosts = await SalaryCostsAsync(keys.Select(m => new Recognition.MonthShare(m, 1m)).ToList(), data.Rates, cancellationToken);

        var lines = await overheads.ListAsync(cancellationToken);
        var result = new List<CompanyMonthResponse>();
        foreach (var month in keys)
        {
            var (overhead, _) = OverheadOf(lines, [(month, 1m)], data.Rates);
            var cost = PeriodMoney.Costs(costs, month, month.AddMonths(1).AddDays(-1));
            var labour = PeriodMoney.Round(cost.Labour);
            var expenses = PeriodMoney.Round(cost.Expenses);
            var materials = PeriodMoney.Round(cost.SiteExpenses);
            var ground = PeriodMoney.Round(cost.GroundLabour);
            var monthRevenue = PeriodMoney.Received(received, month, month.AddMonths(1).AddDays(-1));
            var before = monthRevenue - labour - expenses - materials - ground;
            var (fixedSalaries, paidLeave, premium) = (salaryCosts.Fixed.In(month), salaryCosts.PaidLeave.In(month), salaryCosts.OvertimePremium.In(month));

            result.Add(new CompanyMonthResponse(
                month, monthRevenue, labour, expenses, materials, ground, overhead, fixedSalaries, paidLeave, premium,
                before, before - overhead, before - overhead - fixedSalaries - paidLeave - premium));
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
        => OverheadOf(await overheads.ListAsync(cancellationToken), months, rates);

    private static (decimal Usd, IReadOnlyList<CurrencyTotalResponse> Unconverted) OverheadOf(
        IReadOnlyList<OverheadItem> lines,
        IEnumerable<(DateOnly Month, decimal Share)> months,
        ExchangeRateTable rates)
    {
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
    /// The salaries no project carries, as Salaries works them out, times the share of the month's calendar days the
    /// period covers (like overhead), converted at the rate on the first of the month:
    /// <list type="bullet">
    /// <item>fixed salaries: the whole pay of each month someone was paid without logging hours (<see cref="PaidAsFixed"/>);</item>
    /// <item>paid leave: what a Design salary paid for its paid excused time off (<see cref="SalaryCalculator.PaidLeavePay"/>).
    /// Its logged hours are already project cost;</item>
    /// <item>the overtime premium: what a Design salary paid for overtime past its threshold beyond the hourly rate its
    /// projects carry it at (<see cref="SalaryCalculator.OvertimePremium"/>).</item>
    /// </list>
    /// </summary>
    private async Task<SalaryCosts> SalaryCostsAsync(
        IReadOnlyList<Recognition.MonthShare> shares,
        ExchangeRateTable rates,
        CancellationToken cancellationToken)
    {
        var months = shares.Select(s => s.Month).ToList();
        var people = await payroll.PeopleAsync(DomainCodes.All, includeSuperAdmins: true, domain: null, search: null, months.Min(), cancellationToken);
        var costs = new SalaryCosts(new SalaryLine(), new SalaryLine(), new SalaryLine());

        foreach (var month in await payrollInputs.ForAsync(people, months, cancellationToken))
        {
            if (SalaryCalculator.Calculate(month.Input) is not { } pay)
            {
                continue;
            }

            var (key, currency) = (month.Input.Month, pay.Terms.Currency);
            var share = shares.Single(s => s.Month == key).Calendar;
            if (PaidAsFixed(month))
            {
                costs.Fixed.Add(pay.Total * share, currency, key, rates);
                continue;
            }

            costs.PaidLeave.Add(SalaryCalculator.PaidLeavePay(month.Input, pay) * share, currency, key, rates);
            costs.OvertimePremium.Add(SalaryCalculator.OvertimePremium(pay) * share, currency, key, rates);
        }

        return costs;
    }

    /// <summary>
    /// Whether a month's pay is a fixed salary, a company cost whole: an admin's or super admin's (any month they held
    /// that role on a day), or an employee's outside Design, who logs no hours (Execution's site staff). A Design
    /// employee's logged hours are project cost instead.
    /// </summary>
    private static bool PaidAsFixed(PersonMonth month) => month.Input.FixedSalary || month.User.DomainCode != DomainCodes.Design;

    private static IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices(IEnumerable<IssuedInvoiceRow> invoices)
        => invoices
            .GroupBy(i => i.Currency.ToUpperInvariant())
            .Select(g => new UnconvertedInvoicesResponse(g.Key, g.Sum(i => i.Total), g.Count()))
            .ToList();
}
