using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>A given advance and how it comes off the person's pay.</summary>
public sealed record GivenAdvance(SalaryAdvance Advance, AdvanceRules.Schedule Schedule);

/// <summary>What a month's pay can give an advance: its currency, and the pay (as things stand while the month runs).</summary>
public sealed record MonthPay(string Currency, decimal Available);

/// <summary>Someone's pay this month: what an advance is asked for in and capped at.</summary>
/// <param name="MonthlyPay">The month's pay as a monthly amount, as Salaries shows it: the salary, or an hourly rate × the month's paid hours.</param>
public sealed record PayNow(string Currency, decimal MonthlyPay);

/// <summary>Works out how given advances come off pay, from Salaries' own figures.</summary>
public interface ISalaryAdvanceSchedules
{
    /// <summary>
    /// How each of these advances comes off its person's pay, month by month up to <paramref name="through"/>. Every advance
    /// must be loaded with its months and its person (team and periods of employment). Each month's pay is what Salaries
    /// works out for it: what a closed month came to, and while it runs, the full month as things stand.
    /// </summary>
    /// <param name="known">Pay the caller already worked out, by person and month, so it isn't worked out again.</param>
    Task<IReadOnlyDictionary<long, GivenAdvance>> ScheduleAsync(
        IReadOnlyList<SalaryAdvance> advances,
        DateOnly through,
        IReadOnlyDictionary<(long UserId, DateOnly Month), MonthPay>? known = null,
        CancellationToken cancellationToken = default);

    /// <summary>What a month's pay can give an advance, from Salaries' figures for it.</summary>
    MonthPay PayOf(SalaryCalculator.Result pay, SalaryCalculator.Input input);

    /// <summary>Someone's pay this month; null when they have none set.</summary>
    Task<PayNow?> PayNowAsync(User person, CancellationToken cancellationToken = default);
}
