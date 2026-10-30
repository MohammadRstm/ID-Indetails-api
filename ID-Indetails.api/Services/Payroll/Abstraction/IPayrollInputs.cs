using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>One person's month, ready for <see cref="SalaryCalculator"/>, with the excuses it was built from.</summary>
public sealed record PersonMonth(User User, SalaryCalculator.Input Input, IReadOnlyList<HourExcuse> Excuses);

/// <summary>A work entry split as Salaries pays it.</summary>
/// <param name="MadeUp">Overtime that made up a short month, paid as normal hours.</param>
public sealed record EntrySplit(Overtime.Split Work, int MadeUp)
{
    /// <summary>The overtime paid as overtime: what didn't make up a short month.</summary>
    public int PaidOvertime => Work.Overtime - MadeUp;
}

/// <summary>Loads what pay and hours are calculated from: pay history, working time, schedules, work entries and excuses.</summary>
public interface IPayrollInputs
{
    /// <summary>The company working-time policies, oldest first.</summary>
    Task<IReadOnlyList<PersonCalendar.Policy>> PoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// When someone was employed, from their periods of employment (the user must be loaded with them, as the payroll
    /// repository does). Someone with none counts from when they joined, or their account's creation.
    /// </summary>
    Employment EmploymentOf(User person);

    /// <summary>Each person's working days over time: the company policies with their own schedules, by user id.</summary>
    Task<IReadOnlyDictionary<long, PersonCalendar>> CalendarsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>The roles each person held over time (see <c>Business/Pay/RoleHistory</c>), from their recorded role changes.</summary>
    Task<IReadOnlyDictionary<long, RoleHistory>> RoleHistoriesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// One entry per person and month, in that order; each source is loaded once for everyone and every month. A month
    /// they were an admin or super admin on any day is paid as a fixed salary (<see cref="RoleHistory.FixedSalaryMonth"/>),
    /// whatever their role is now.
    /// </summary>
    Task<IReadOnlyList<PersonMonth>> ForAsync(IReadOnlyList<User> people, IReadOnlyList<DateOnly> months, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every work entry these people logged in these months, split as Salaries pays it, by entry id: overtime is decided
    /// per day across all their work, overtime that made up a short month counts as normal hours, and a month paid as a
    /// fixed salary has no overtime. Project pay and project cost price entries from it.
    /// </summary>
    Task<IReadOnlyDictionary<long, EntrySplit>> EntrySplitsAsync(IReadOnlyList<User> people, IReadOnlyList<DateOnly> months, CancellationToken cancellationToken = default);
}
