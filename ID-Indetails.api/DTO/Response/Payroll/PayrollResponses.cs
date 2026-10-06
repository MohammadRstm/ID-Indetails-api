using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Payroll;

/// <summary>
/// One person's month. Minutes are hours worked or owed; money is in <see cref="Currency"/>.
/// The hours-ledger fields (<see cref="ExpectedMinutes"/>, <see cref="ShortMinutes"/>) are null for people
/// it doesn't apply to (anyone but Design employees).
/// </summary>
public sealed record SalaryRowResponse
{
    public required long UserId { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }

    public string? Domain { get; init; }

    public TeamSummaryResponse? Team { get; init; }

    public required PayBasis Basis { get; init; }

    /// <summary>Monthly salary or hourly rate.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>What an hour is worth this month (a salary ÷ the month's paid hours): it prices deductions.</summary>
    public required decimal HourlyRate { get; init; }

    /// <summary>The true hourly rate, the same every month (a salary ÷ a standard month's paid hours): overtime is paid at it × the multiplier.</summary>
    public required decimal StandardHourlyRate { get; init; }

    /// <summary>False for Execution staff, admins and super admins, who don't log hours.</summary>
    public required bool TracksHours { get; init; }

    public int? ExpectedMinutes { get; init; }

    public required int NormalMinutes { get; init; }

    public required int MadeUpMinutes { get; init; }

    /// <summary>Overtime paid at the multiplier (after any made up a shortfall).</summary>
    public required int OvertimeMinutes { get; init; }

    public required int ExcusedMinutes { get; init; }

    public int? ShortMinutes { get; init; }

    /// <summary>A closed month still short of hours.</summary>
    public required bool Flagged { get; init; }

    /// <summary>The month in progress is short so far.</summary>
    public required bool Behind { get; init; }

    public required bool ProRated { get; init; }

    /// <summary>The day they left (their account was deactivated); their pay and hours end there. Null while active.</summary>
    public DateOnly? LeftOn { get; init; }

    public long? PhotoFileId { get; init; }

    public string? Position { get; init; }

    public required int WorkingDays { get; init; }

    public required int WorkingDaysPaid { get; init; }

    public required decimal Deduction { get; init; }

    public required decimal BasePay { get; init; }

    public required decimal OvertimePay { get; init; }

    public required decimal Total { get; init; }

    /// <summary>The month's pay as a monthly amount: the salary, or an hourly rate × the month's paid hours on their schedule.</summary>
    public required decimal MonthlyPay { get; init; }

    /// <summary>
    /// While a salaried month runs, the salary accrued so far plus overtime pay (as My pay shows it); otherwise
    /// <see cref="Total"/>.
    /// </summary>
    public required decimal EarnedSoFar { get; init; }

    /// <summary>While a salaried month runs, what it comes to if things stay as they are; null otherwise.</summary>
    public decimal? FullMonthEstimate { get; init; }
}

/// <param name="EarnedSoFar">The rows' <see cref="SalaryRowResponse.EarnedSoFar"/>: equal to <paramref name="Total"/> once the month has closed.</param>
public sealed record SalaryTotalResponse(string Currency, int People, decimal BasePay, decimal OvertimePay, decimal Deduction, decimal Total, decimal EarnedSoFar);

/// <param name="ExpectedMinutes">Hours the schedules ask for, of the people under the hours rule; null when nobody listed is.</param>
public sealed record SalarySummaryResponse(
    int People,
    int OnMonthlySalary,
    int NormalMinutes,
    int OvertimeMinutes,
    int MadeUpMinutes,
    int ShortPeople,
    int ShortMinutes,
    int? ExpectedMinutes);

/// <summary>Someone employed in the month with no pay set, so they have no salary to list.</summary>
public sealed record PersonWithoutPayResponse(long UserId, string Name, string Role, string? Domain);

/// <param name="Month">First day of the month.</param>
/// <param name="Totals">One line per currency; currencies are never added together.</param>
/// <param name="WithoutPay">People of the filtered list left out because no pay is set for them.</param>
public sealed record SalariesResponse(
    DateOnly Month,
    bool InProgress,
    decimal OvertimeMultiplier,
    PagedResponse<SalaryRowResponse> Rows,
    IReadOnlyList<SalaryTotalResponse> Totals,
    SalarySummaryResponse Summary,
    IReadOnlyList<PersonWithoutPayResponse> WithoutPay);

/// <summary>What someone is owed for one month of the year.</summary>
/// <param name="Amount">The monthly salary or hourly rate in force, in <paramref name="Currency"/>.</param>
/// <param name="Total">What they are owed that month, as Salaries works it out.</param>
public sealed record SalaryYearMonthResponse(DateOnly Month, PayBasis Basis, decimal Amount, string Currency, decimal Total);

/// <param name="Months">Only the months they have a salary for.</param>
public sealed record SalaryYearRowResponse(
    long UserId,
    string Name,
    string Email,
    string Role,
    string? Domain,
    TeamSummaryResponse? Team,
    long? PhotoFileId,
    string? Position,
    IReadOnlyList<SalaryYearMonthResponse> Months);

/// <summary>One month's total in one currency.</summary>
public sealed record SalaryYearTotalResponse(DateOnly Month, string Currency, int People, decimal Total);

/// <summary>A year of salaries, person by person and month by month, up to this month.</summary>
/// <param name="Months">The first day of each month shown, oldest first.</param>
/// <param name="Totals">Per month and currency; currencies are never added together.</param>
public sealed record SalaryYearResponse(
    int Year,
    IReadOnlyList<DateOnly> Months,
    IReadOnlyList<SalaryYearRowResponse> Rows,
    IReadOnlyList<SalaryYearTotalResponse> Totals);

public sealed record OvertimeWorkResponse(long WorkEntryId, DateOnly Day, int Minutes, int OvertimeMinutes, int MadeUpMinutes);

public sealed record HourExcuseResponse(
    long Id,
    DateOnly Month,
    ExcuseKind Kind,
    int? Minutes,
    IReadOnlyList<DateOnly> Days,
    bool IsPaid,
    string Note,
    DateTime CreatedAt,
    long? CreatedById);

/// <summary>A working day of someone's month, with the minutes it requires.</summary>
public sealed record SalaryDayResponse(DateOnly Date, int RequiredMinutes);

/// <summary>
/// The hours someone worked on one project in the month, and what they were paid for them (as on Project pay:
/// hours within schedule and made-up hours at their rate that day, the rest of the overtime at the multiplier).
/// </summary>
/// <param name="NormalMinutes">Worked within their scheduled hours.</param>
/// <param name="MadeUpMinutes">Overtime that made up hours they were short, paid as normal hours.</param>
/// <param name="OvertimeMinutes">The rest of the overtime, paid at the overtime rate.</param>
/// <param name="Pay">In <paramref name="Currency"/>; a person paid in two currencies that month has a row for each.</param>
public sealed record SalaryProjectResponse(
    long ProjectId,
    string Title,
    string? Icon,
    string? Tint,
    int Entries,
    int NormalMinutes,
    int MadeUpMinutes,
    int OvertimeMinutes,
    decimal Pay,
    string Currency);

/// <param name="MissedDays">Working days with no work within hours and no excuse.</param>
/// <param name="DeductionMinutes">Shortfall (with the paid break of each missed day) and unpaid excuses.</param>
/// <param name="WorkingDays">The month's working days while the person was employed, to choose days to excuse from.</param>
/// <param name="Projects">Where their hours and overtime went, most time first. Empty for people who don't log hours.</param>
public sealed record SalaryDetailResponse(
    SalaryRowResponse Salary,
    bool InProgress,
    int PaidBreakDays,
    decimal PaidBreakPay,
    int DeductionMinutes,
    IReadOnlyList<DateOnly> MissedDays,
    IReadOnlyList<OvertimeWorkResponse> OvertimeWork,
    IReadOnlyList<HourExcuseResponse> Excuses,
    IReadOnlyList<SalaryDayResponse> WorkingDays,
    IReadOnlyList<SalaryProjectResponse> Projects);
