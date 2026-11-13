using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Payroll;

/// <param name="LeftOn">Their last working day, once they have left.</param>
public sealed record SalaryAdvancePersonResponse(
    long Id,
    string Name,
    long? PhotoFileId,
    string Role,
    string? Domain,
    string? DepartmentName,
    DateOnly? LeftOn);

/// <param name="Month">The first day of the month.</param>
/// <param name="Planned">What the month was set to take.</param>
/// <param name="Taken">
/// What came off the month's pay: its plan plus what earlier months couldn't take, never more than the pay; as things
/// stand while the month runs. Null for a month not reached yet, and always null in the person's own list.
/// </param>
/// <param name="Closed">The month is over.</param>
public sealed record SalaryAdvanceMonthResponse(DateOnly Month, decimal Planned, decimal? Taken, bool Closed);

public sealed record SalaryAdvanceSettlementResponse(
    SalaryAdvanceSettlement Kind,
    decimal Amount,
    DateOnly SettledOn,
    string? SettledByName,
    string? Note);

/// <summary>A salary advance as super admins see it.</summary>
/// <param name="AskedAmount">What the person asked for; null when it was recorded directly.</param>
/// <param name="RecordedByName">Who recorded it, when a super admin did without a request.</param>
/// <param name="Months">Every month it plans to take from, and any it took from beyond them.</param>
/// <param name="Repaid">What has come off pay so far, this month as things stand; null until it is given.</param>
/// <param name="Left">What is still to come off pay after that; null until it is given, 0 once settled.</param>
/// <param name="Behind">A closed month couldn't take all it was due: the rest moved on.</param>
/// <param name="Unsettled">The person has left and something is still left: settle it.</param>
/// <param name="CanDecide">The caller may approve or decline this request.</param>
/// <param name="CanChange">The caller may change its months or settle what is left.</param>
/// <param name="CanCancel">The caller may cancel it: it was given this month.</param>
public sealed record SalaryAdvanceResponse(
    long Id,
    SalaryAdvancePersonResponse Person,
    SalaryAdvanceStatus Status,
    decimal Amount,
    decimal? AskedAmount,
    string Currency,
    string? Reason,
    DateTime CreatedAt,
    string? RecordedByName,
    DateTime? DecidedAt,
    string? DecidedByName,
    string? DecisionNote,
    DateOnly? GivenOn,
    IReadOnlyList<SalaryAdvanceMonthResponse> Months,
    decimal? Repaid,
    decimal? Left,
    bool Behind,
    bool Unsettled,
    SalaryAdvanceSettlementResponse? Settlement,
    DateTime? CancelledAt,
    bool CanDecide,
    bool CanChange,
    bool CanCancel);

/// <summary>An advance in the person's own list: never their pay, nor who decided.</summary>
/// <param name="Left">What is still to come off their pay; null until it is given.</param>
public sealed record MySalaryAdvanceResponse(
    long Id,
    SalaryAdvanceStatus Status,
    decimal Amount,
    decimal? AskedAmount,
    string Currency,
    string? Reason,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    string? DecisionNote,
    DateOnly? GivenOn,
    IReadOnlyList<SalaryAdvanceMonthResponse> Months,
    decimal? Left,
    SalaryAdvanceSettlement? SettledKind,
    DateOnly? SettledOn,
    DateTime? CancelledAt);

/// <param name="CanAsk">Whether they may ask for one now.</param>
/// <param name="CannotAskBecause">Why not: <c>salary_advance_no_pay</c>, <c>salary_advance_pending</c> or <c>salary_advance_open</c>.</param>
/// <param name="Currency">The currency of their pay, which an advance is asked for in; null with no pay set.</param>
public sealed record MySalaryAdvancesResponse(
    bool CanAsk,
    string? CannotAskBecause,
    string? Currency,
    IReadOnlyList<MySalaryAdvanceResponse> Items);

/// <summary>Advances at a glance, for Salaries.</summary>
/// <param name="Pending">Requests waiting for a decision.</param>
/// <param name="Left">What is still to come off pay, per currency.</param>
/// <param name="Unsettled">People who left with something still to settle.</param>
public sealed record SalaryAdvanceSummaryResponse(int Pending, IReadOnlyList<CurrencyAmountResponse> Left, int Unsettled);
