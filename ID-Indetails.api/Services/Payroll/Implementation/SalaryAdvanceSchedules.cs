using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// Works out each month an advance comes off from what Salaries says the month pays. Only the months something is due
/// in are worked out: first the planned months and the months the person left in, then any month a shortfall moved on to.
/// </summary>
public sealed class SalaryAdvanceSchedules(IPayrollInputs inputs, IClock clock) : ISalaryAdvanceSchedules
{
    public async Task<IReadOnlyDictionary<long, GivenAdvance>> ScheduleAsync(
        IReadOnlyList<SalaryAdvance> advances,
        DateOnly through,
        IReadOnlyDictionary<(long UserId, DateOnly Month), MonthPay>? known = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, GivenAdvance>();
        foreach (var personAdvances in advances.Where(a => a.Repayments.Count > 0).GroupBy(a => a.UserId))
        {
            var person = personAdvances.First().User;
            var ordered = personAdvances.OrderBy(a => a.GivenOn).ThenBy(a => a.Id).ToList();
            var rules = ordered.Select(ToRules).ToList();
            var employment = inputs.EmploymentOf(person);
            var leaving = employment.Periods
                .Where(p => p.EndedOn is { } ended && ended <= through.AddMonths(1).AddDays(-1))
                .Select(p => AdvanceRules.MonthOf(p.EndedOn!.Value))
                .ToHashSet();

            // what each month's pay can give, per currency; months they weren't employed give nothing
            var pay = new Dictionary<DateOnly, MonthPay>();
            foreach (var month in Months(rules, through).Where(m => !EmployedIn(employment, m)))
            {
                pay[month] = new MonthPay(string.Empty, 0);
            }

            foreach (var ((userId, month), monthPay) in known ?? new Dictionary<(long, DateOnly), MonthPay>())
            {
                if (userId == person.Id)
                {
                    pay[month] = monthPay;
                }
            }

            var needed = rules.SelectMany(r => r.Planned.Keys).Concat(leaving).Where(m => m <= through && !pay.ContainsKey(m)).Distinct().ToList();
            await AddPayAsync(person, needed, pay, cancellationToken);

            var schedules = Schedules(ordered, rules, pay, leaving, through);
            var missing = schedules.Values.SelectMany(s => s.Missing).Distinct().ToList();
            if (missing.Count > 0)
            {
                await AddPayAsync(person, missing, pay, cancellationToken);
                schedules = Schedules(ordered, rules, pay, leaving, through);
            }

            foreach (var advance in ordered)
            {
                result[advance.Id] = new GivenAdvance(advance, schedules[advance.Id]);
            }
        }

        return result;
    }

    public MonthPay PayOf(SalaryCalculator.Result pay, SalaryCalculator.Input input)
    {
        var inProgress = clock.CompanyToday >= input.Month && clock.CompanyToday < input.Month.AddMonths(1);
        if (!inProgress)
        {
            return new MonthPay(pay.Terms.Currency, pay.Total);
        }

        // while the month runs it counts as a whole month: a salary as it stands, hourly pay at the month's paid hours
        var fullMonth = MyTime.EstimateOf(pay, input, inProgress: true).FullMonth;
        return new MonthPay(pay.Terms.Currency, fullMonth ?? Math.Max(pay.Total, SalaryCalculator.MonthlyPay(pay, input)));
    }

    public async Task<PayNow?> PayNowAsync(User person, CancellationToken cancellationToken = default)
    {
        var month = AdvanceRules.MonthOf(clock.CompanyToday);
        var personMonth = (await inputs.ForAsync([person], [month], cancellationToken)).Single();
        return SalaryCalculator.Calculate(personMonth.Input) is { } pay
            ? new PayNow(pay.Terms.Currency, SalaryCalculator.MonthlyPay(pay, personMonth.Input))
            : null;
    }

    private static AdvanceRules.Advance ToRules(SalaryAdvance advance)
        => new(advance.Id, advance.Amount, advance.Repayments.ToDictionary(r => r.Month, r => r.Amount), advance.SettledOn);

    /// <summary>Each currency's advances take from a month's pay only when the month is paid in that currency.</summary>
    private static Dictionary<long, AdvanceRules.Schedule> Schedules(
        IReadOnlyList<SalaryAdvance> advances,
        IReadOnlyList<AdvanceRules.Advance> rules,
        IReadOnlyDictionary<DateOnly, MonthPay> pay,
        IReadOnlySet<DateOnly> leaving,
        DateOnly through)
    {
        var schedules = new Dictionary<long, AdvanceRules.Schedule>();
        foreach (var currency in advances.Select(a => a.Currency).Distinct())
        {
            var ofCurrency = rules.Where(r => advances.Single(a => a.Id == r.Id).Currency == currency).ToList();
            var available = pay.ToDictionary(p => p.Key, p => p.Value.Currency == currency ? p.Value.Available : 0);
            foreach (var (id, schedule) in AdvanceRules.Repayments(ofCurrency, available, leaving, through))
            {
                schedules[id] = schedule;
            }
        }

        return schedules;
    }

    private async Task AddPayAsync(User person, IReadOnlyList<DateOnly> months, Dictionary<DateOnly, MonthPay> pay, CancellationToken cancellationToken)
    {
        if (months.Count == 0)
        {
            return;
        }

        foreach (var personMonth in await inputs.ForAsync([person], months, cancellationToken))
        {
            pay[personMonth.Input.Month] = SalaryCalculator.Calculate(personMonth.Input) is { } result
                ? PayOf(result, personMonth.Input)
                : new MonthPay(string.Empty, 0);
        }
    }

    /// <summary>Every month from the first one planned up to <paramref name="through"/>.</summary>
    private static IEnumerable<DateOnly> Months(IReadOnlyList<AdvanceRules.Advance> rules, DateOnly through)
    {
        if (rules.Count == 0)
        {
            yield break;
        }

        for (var month = rules.SelectMany(r => r.Planned.Keys).Min(); month <= through; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static bool EmployedIn(Employment employment, DateOnly month)
    {
        var last = month.AddMonths(1).AddDays(-1);
        return employment.Periods.Any(p => p.StartedOn <= last && (p.EndedOn is null || p.EndedOn >= month));
    }
}
