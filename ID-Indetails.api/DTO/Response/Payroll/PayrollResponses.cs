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

    public required decimal HourlyRate { get; init; }

    /// <summary>False for Execution staff, who don't log hours.</summary>
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

    public required int WorkingDays { get; init; }

    public required int WorkingDaysPaid { get; init; }

    public required decimal Deduction { get; init; }

    public required decimal BasePay { get; init; }

    public required decimal OvertimePay { get; init; }

    public required decimal Total { get; init; }
}

public sealed record SalaryTotalResponse(string Currency, int People, decimal BasePay, decimal OvertimePay, decimal Deduction, decimal Total);

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

/// <param name="Month">First day of the month.</param>
/// <param name="Totals">One line per currency; currencies are never added together.</param>
public sealed record SalariesResponse(
    DateOnly Month,
    bool InProgress,
    decimal OvertimeMultiplier,
    PagedResponse<SalaryRowResponse> Rows,
    IReadOnlyList<SalaryTotalResponse> Totals,
    SalarySummaryResponse Summary);

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

/// <param name="MissedDays">Working days with no work within hours and no excuse.</param>
/// <param name="DeductionMinutes">Shortfall (with the paid break of each missed day) and unpaid excuses.</param>
/// <param name="WorkingDays">The month's working days while the person was employed, to choose days to excuse from.</param>
public sealed record SalaryDetailResponse(
    SalaryRowResponse Salary,
    bool InProgress,
    int PaidBreakDays,
    decimal PaidBreakPay,
    int DeductionMinutes,
    IReadOnlyList<DateOnly> MissedDays,
    IReadOnlyList<OvertimeWorkResponse> OvertimeWork,
    IReadOnlyList<HourExcuseResponse> Excuses,
    IReadOnlyList<SalaryDayResponse> WorkingDays);
