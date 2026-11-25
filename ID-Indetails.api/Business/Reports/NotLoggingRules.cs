using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;

namespace ID_Indetails.api.Business.Reports;

/// <summary>
/// The Design employees who have logged no work since the start of this week: the dashboard's "not logging" item and the
/// page behind it both choose them here, so their count can never differ. Their hours this week come from the same
/// calculators as the month's hours ledger, never a second formula.
/// </summary>
public static class NotLoggingRules
{
    /// <summary>One person's week so far, in minutes.</summary>
    /// <param name="Logged">The minutes logged, plus the break taken on days where the break counts, as the hours ledger counts them.</param>
    /// <param name="Expected">What there was to log on the working days they were employed and not excused.</param>
    /// <param name="Overtime">The overtime of the work logged.</param>
    public sealed record WeekHours(int Logged, int Expected, int Overtime);

    /// <summary>Active Design employees (by role, not permission) who are not among <paramref name="loggingThisWeek"/>, by name.</summary>
    public static IReadOnlyList<DesignPerson> Of(IEnumerable<DesignPerson> people, IReadOnlySet<long> loggingThisWeek)
        => people
            .Where(p => p.Status == UserStatus.Active && p.Role == RoleCodes.Employee && !loggingThisWeek.Contains(p.Id))
            .OrderBy(p => p.Name)
            .ToList();

    /// <summary>
    /// Someone's hours from <paramref name="weekStart"/> to <paramref name="today"/>, from the inputs of each month those days
    /// touch (the hours ledger's, see <see cref="HoursLedger"/>). A month the ledger doesn't apply to counts nothing.
    /// </summary>
    public static WeekHours HoursOf(DateOnly weekStart, DateOnly today, IEnumerable<SalaryCalculator.Input> months)
    {
        int logged = 0, expected = 0, overtime = 0;
        foreach (var input in months.Where(m => m.LedgerApplies))
        {
            var from = weekStart > input.Month ? weekStart : input.Month;
            var next = input.Month.AddMonths(1);
            var until = today.AddDays(1) < next ? today.AddDays(1) : next;
            if (from >= until)
            {
                continue;
            }

            var work = Overtime.Of(input.Work, input.Calendar).Where(w => w.Day >= from && w.Day < until).ToList();
            var days = HoursLedger.WorkingDaysIn(from, until, input.JoinedOn, input.LeftOn, input.Calendar, input.Gaps);
            var excusedDays = input.Excuses.SelectMany(e => e.Days).ToHashSet();

            logged += work.Sum(w => w.Minutes) + HoursLedger.BreaksTaken(days, work, excusedDays);
            overtime += work.Sum(w => w.Overtime);
            expected += input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth)
                ? 0
                : HoursLedger.ExpectedOf(days) - HoursLedger.ExcusedDaysOf(days, excusedDays);
        }

        return new WeekHours(logged, expected, overtime);
    }
}
