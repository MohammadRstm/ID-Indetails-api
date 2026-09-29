using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>One person's month, ready for <see cref="SalaryCalculator"/>, with the excuses it was built from.</summary>
public sealed record PersonMonth(User User, SalaryCalculator.Input Input, IReadOnlyList<HourExcuse> Excuses);

/// <summary>Loads what pay and hours are calculated from: pay history, working time, schedules, work entries and excuses.</summary>
public interface IPayrollInputs
{
    /// <summary>The company working-time policies, oldest first.</summary>
    Task<IReadOnlyList<PersonCalendar.Policy>> PoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>The day someone joined; people set up without one (the first super admin) count from their account's creation.</summary>
    DateOnly JoinedOn(User person);

    /// <summary>One entry per person and month, in that order; each source is loaded once for everyone and every month.</summary>
    Task<IReadOnlyList<PersonMonth>> ForAsync(IReadOnlyList<User> people, IReadOnlyList<DateOnly> months, CancellationToken cancellationToken = default);
}
