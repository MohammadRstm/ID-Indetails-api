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
/// <param name="TotalMonths">Months from the one the person joined to this one (or the one they left).</param>
public sealed record MyHoursResponse(bool Applies, int Page, int PageSize, int TotalMonths, IReadOnlyList<MyLedgerMonthResponse> Months);

/// <param name="MadeUpMinutes">
/// Part of its overtime that made up missing hours; null where the ledger doesn't apply, or on someone else's overtime
/// for a caller without salary:view.
/// </param>
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

/// <param name="MadeUpMinutes">Overtime that made up missing hours; null as on each entry.</param>
/// <param name="PaidMinutes">Overtime paid as overtime; null as on each entry.</param>
/// <param name="Days">Days with overtime, newest first.</param>
public sealed record MyOvertimeResponse(
    DateOnly Month,
    bool InProgress,
    int OvertimeMinutes,
    int? MadeUpMinutes,
    int? PaidMinutes,
    int Entries,
    IReadOnlyList<MyOvertimeDayResponse> Days);

/// <summary>One day of the caller's schedule and what they logged on it.</summary>
/// <param name="IsWorkingDay">False on a day off: anything logged is overtime.</param>
/// <param name="LeftMinutes">Required minutes not yet covered by work within hours.</param>
/// <param name="MonthShortMinutes">
/// What the day's month is short of its required hours so far (null where the hours ledger doesn't apply). Overtime
/// logged in the month first makes this up, as normal hours.
/// </param>
public sealed record MyScheduleDayResponse(
    DateOnly Date,
    bool IsWorkingDay,
    int? StartMinute,
    int? EndMinute,
    int RequiredMinutes,
    int PaidBreakMinutes,
    int LoggedMinutes,
    int InsideMinutes,
    int OvertimeMinutes,
    int LeftMinutes,
    int? MonthShortMinutes);

/// <param name="Schedule">The caller's week: their own schedule where they have one, otherwise the company week.</param>
/// <param name="CompanyWeek">The company policy in force: its break and overtime multiplier apply to everyone.</param>
/// <param name="Day">The day asked for (today by default).</param>
public sealed record MyScheduleResponse(PersonScheduleResponse Schedule, WorkingTimePolicyResponse CompanyWeek, MyScheduleDayResponse Day);

/// <summary>An excuse in the caller's month; who made it isn't shown.</summary>
public sealed record MyExcuseResponse(ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid, string Note);

/// <summary>
/// The overtime of work about to be logged, and where it falls, for the log form's warning. The month's shortfall that
/// overtime first makes up is <c>monthShortMinutes</c> on <c>GET /api/me/schedule</c>.
/// </summary>
/// <param name="DayOff">A day off: every minute is overtime, and the other fields are 0 or null.</param>
/// <param name="AfterHoursMinutes">After the day's end, time past midnight included.</param>
/// <param name="PastRequiredMinutes">Within the hours, beyond what the day asks for.</param>
/// <param name="DayStartMinute">The day's hours, in minutes after midnight (company time).</param>
public sealed record OvertimePreviewResponse(
    int Minutes,
    int OvertimeMinutes,
    bool DayOff,
    int BeforeHoursMinutes,
    int AfterHoursMinutes,
    int PastRequiredMinutes,
    int? DayStartMinute,
    int? DayEndMinute,
    int? RequiredMinutes);
