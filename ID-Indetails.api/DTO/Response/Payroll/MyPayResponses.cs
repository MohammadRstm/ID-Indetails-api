using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Payroll;

/// <summary>
/// One month of the caller's hours ledger. <see cref="ExpectedMinutes"/> runs up to yesterday while the month is
/// in progress; <see cref="ExpectedFullMonthMinutes"/> is the whole month.
/// </summary>
/// <param name="InsideMinutes">Worked within the caller's hours, up to what each day requires.</param>
/// <param name="MadeUpMinutes">Overtime that made up missing hours.</param>
/// <param name="OvertimePaidMinutes">Overtime left after that, paid as overtime.</param>
/// <param name="MissedDays">Working days with no work within hours and no excuse.</param>
/// <param name="Excuses">The month's excuses, with their notes.</param>
public sealed record MyLedgerMonthResponse(
    DateOnly Month,
    bool InProgress,
    MyTime.LedgerStatus Status,
    int ExpectedMinutes,
    int ExpectedFullMonthMinutes,
    int ExcusedMinutes,
    bool WholeMonthExcused,
    int InsideMinutes,
    int MadeUpMinutes,
    int OvertimePaidMinutes,
    int ShortMinutes,
    IReadOnlyList<DateOnly> MissedDays,
    IReadOnlyList<MyExcuseResponse> Excuses);

/// <param name="Applies">The hours ledger applies to Design employees only; for anyone else there are no months.</param>
/// <param name="TotalMonths">Months from the one the caller joined to this one.</param>
public sealed record MyHoursResponse(bool Applies, int Page, int PageSize, int TotalMonths, IReadOnlyList<MyLedgerMonthResponse> Months);

/// <param name="MadeUpMinutes">Part of its overtime that made up missing hours; null where the ledger doesn't apply.</param>
public sealed record MyOvertimeEntryResponse(
    long WorkEntryId,
    long ProjectId,
    string ProjectTitle,
    string? ProjectIcon,
    string? ProjectTint,
    string Description,
    DateTime StartedAt,
    DateTime EndedAt,
    int Minutes,
    int OvertimeMinutes,
    int? MadeUpMinutes);

public sealed record MyOvertimeDayResponse(DateOnly Date, bool DayOff, int OvertimeMinutes, IReadOnlyList<MyOvertimeEntryResponse> Entries);

/// <param name="MadeUpMinutes">Overtime that made up missing hours; null where the ledger doesn't apply.</param>
/// <param name="PaidMinutes">Overtime paid as overtime; null where the ledger doesn't apply.</param>
/// <param name="Days">Days with overtime, newest first.</param>
public sealed record MyOvertimeResponse(
    DateOnly Month,
    bool InProgress,
    int OvertimeMinutes,
    int? MadeUpMinutes,
    int? PaidMinutes,
    int Entries,
    IReadOnlyList<MyOvertimeDayResponse> Days);

/// <param name="IsWorkingDay">False on a day off: anything logged is overtime.</param>
/// <param name="LeftMinutes">Required minutes not yet covered by work within hours.</param>
public sealed record MyTodayResponse(
    DateOnly Date,
    bool IsWorkingDay,
    int? StartMinute,
    int? EndMinute,
    int RequiredMinutes,
    int PaidBreakMinutes,
    int LoggedMinutes,
    int InsideMinutes,
    int OvertimeMinutes,
    int LeftMinutes);

/// <param name="Schedule">The caller's week: their own schedule where they have one, otherwise the company week.</param>
/// <param name="CompanyWeek">The company policy in force: its break and overtime multiplier apply to everyone.</param>
public sealed record MyScheduleResponse(PersonScheduleResponse Schedule, WorkingTimePolicyResponse CompanyWeek, MyTodayResponse Today);

/// <param name="DifferenceMinutes">Inside hours − required; null where the day isn't judged (today, excused, a day off).</param>
public sealed record MyPayDayResponse(
    DateOnly Date,
    int RequiredMinutes,
    int LoggedMinutes,
    int InsideMinutes,
    int OvertimeMinutes,
    bool Excused,
    bool DayOff,
    bool IsToday,
    int? DifferenceMinutes);

/// <summary>An excuse in the caller's month; who made it isn't shown.</summary>
public sealed record MyExcuseResponse(ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid, string Note);

/// <summary>The caller's pay for the month, as the Salaries screen calculates it. Money is in <see cref="Currency"/>.</summary>
public sealed record MySalaryResponse
{
    public required PayBasis Basis { get; init; }

    /// <summary>Monthly salary or hourly rate.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required decimal HourlyRate { get; init; }

    public required decimal OvertimeMultiplier { get; init; }

    public required int NormalMinutes { get; init; }

    public required int MadeUpMinutes { get; init; }

    /// <summary>Overtime paid at the multiplier (after any that made up a shortfall).</summary>
    public required int OvertimeMinutes { get; init; }

    public required int ExcusedMinutes { get; init; }

    public required bool ProRated { get; init; }

    public required int WorkingDays { get; init; }

    public required int WorkingDaysPaid { get; init; }

    /// <summary>Hourly pay: working days worked, each paid its break on top.</summary>
    public required int PaidBreakDays { get; init; }

    public required decimal PaidBreakPay { get; init; }

    /// <summary>Shortfall (with the paid break of each missed day) and unpaid excuses.</summary>
    public required int DeductionMinutes { get; init; }

    public required decimal Deduction { get; init; }

    public required decimal BasePay { get; init; }

    public required decimal OvertimePay { get; init; }

    public required decimal Total { get; init; }

    /// <summary>
    /// What has been earned by today while the month runs: for salaried staff, the salary accrued so far (by the hours
    /// required so far, or by the working days passed where the hours ledger doesn't apply) plus overtime pay. The
    /// total once the month has closed.
    /// </summary>
    public required decimal EarnedSoFar { get; init; }

    /// <summary>Salaried, month in progress: salary + overtime so far − <see cref="WouldLose"/>. An estimate.</summary>
    public decimal? FullMonthEstimate { get; init; }

    /// <summary>
    /// Salaried, month in progress: what would come off if the month closed as short as it is now - the shortfall
    /// plus the paid break of each whole missed day, at the hourly rate, as Salaries deducts it.
    /// </summary>
    public required decimal WouldLose { get; init; }
}

/// <param name="JoinedOn">The day the caller joined.</param>
/// <param name="FirstMonth">The month the caller joined: the earliest one to ask for.</param>
/// <param name="Salary">Null when the caller has no pay record for the month.</param>
/// <param name="Ledger">Null where the hours ledger doesn't apply (anyone but Design employees).</param>
/// <param name="Days">The month's days up to today, newest first.</param>
public sealed record MyPayResponse(
    DateOnly Month,
    bool InProgress,
    DateOnly JoinedOn,
    DateOnly FirstMonth,
    MySalaryResponse? Salary,
    MyLedgerMonthResponse? Ledger,
    IReadOnlyList<MyPayDayResponse> Days,
    IReadOnlyList<MyExcuseResponse> Excuses);
