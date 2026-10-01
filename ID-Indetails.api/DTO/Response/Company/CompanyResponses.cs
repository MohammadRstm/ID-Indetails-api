using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Company;

/// <param name="IsSetUp">False until a super admin first saves the settings; the other values are defaults until then.</param>
/// <param name="TimeZone">From configuration; every calendar date in the system is in this zone.</param>
/// <param name="LogoFileId">The company logo, read through <c>GET /api/files/{id}</c> (or <c>GET /api/company/logo</c>).</param>
public sealed record CompanySettingsResponse(
    bool IsSetUp,
    string Name,
    string? Address,
    string? Website,
    long? LogoFileId,
    string TimeZone,
    byte WeekStart,
    bool Use24HourTime,
    string DefaultCurrency,
    DateTime? UpdatedAt);

/// <summary>What the sign-in page shows before anyone signs in.</summary>
/// <param name="Name">Null until the company settings are saved.</param>
/// <param name="HasLogo">Whether <c>GET /api/company/logo</c> has an image.</param>
/// <param name="LogoFileId">The logo's file, which changes with every new logo: a client can add it to the logo's address so a cached image is never shown.</param>
public sealed record BrandingResponse(string? Name, bool HasLogo, long? LogoFileId);

/// <param name="RequiredMinutesPerDay">Hours required on a working day: the span minus the break (the break is paid).</param>
public sealed record WorkingTimePolicyResponse(
    long Id,
    DateOnly EffectiveFrom,
    IReadOnlyList<int> WorkDays,
    int StartMinute,
    int EndMinute,
    int BreakMinutes,
    decimal OvertimeMultiplier,
    int RequiredMinutesPerDay,
    int RequiredMinutesPerWeek);

/// <param name="Policies">Every policy, newest first, including ones that haven't started yet.</param>
public sealed record WorkingTimeResponse(WorkingTimePolicyResponse Current, IReadOnlyList<WorkingTimePolicyResponse> Policies);

public sealed record ScheduleDayResponse(int Weekday, int StartMinute, int EndMinute, int RequiredMinutes);

/// <param name="IsOwn">False when the person follows the company week.</param>
/// <param name="NextChangeOn">A change that is set but hasn't started yet.</param>
/// <param name="ChangedOn">The day the schedule in force started (their own, or back to the company week); null when it was
/// never changed. A change starting that day can't be replaced, so after one made today the next starts tomorrow.</param>
public sealed record PersonScheduleResponse(
    long UserId,
    string Name,
    string? Position,
    string Role,
    string? Domain,
    TeamSummaryResponse? Team,
    bool IsOwn,
    DateOnly? OwnSince,
    IReadOnlyList<ScheduleDayResponse> Days,
    int RequiredMinutesPerWeek,
    DateOnly? NextChangeOn,
    long? PhotoFileId,
    DateOnly? ChangedOn);

public sealed record SchedulesResponse(WorkingTimePolicyResponse CompanyWeek, IReadOnlyList<PersonScheduleResponse> People);

public sealed record OverheadAmountResponse(DateOnly EffectiveFrom, decimal Amount);

/// <param name="CurrentAmount">What it costs this month; null when it isn't charged this month.</param>
/// <param name="PerYear">Twelve months at the current amount, while it runs.</param>
public sealed record OverheadLineResponse(
    long Id,
    string Name,
    OverheadGroupSummaryResponse Group,
    string Currency,
    decimal? CurrentAmount,
    decimal? PerYear,
    DateOnly StartedOn,
    DateOnly? EndedOn,
    bool IsCancelled,
    IReadOnlyList<OverheadAmountResponse> Amounts);

public sealed record OverheadGroupSummaryResponse(long Id, string Name);

/// <param name="Lines">How many lines use it, past ones included; only one used by none can be removed.</param>
public sealed record OverheadGroupResponse(long Id, string Name, int Lines);

public sealed record OverheadGroupTotalResponse(long GroupId, string Name, string Currency, decimal Total);

/// <param name="MonthTotals">This month's overhead, one total per currency.</param>
public sealed record OverheadResponse(
    IReadOnlyList<OverheadLineResponse> Lines,
    IReadOnlyList<CurrencyTotalResponse> MonthTotals,
    IReadOnlyList<OverheadGroupTotalResponse> GroupTotals);

/// <param name="Month">First day of the month.</param>
public sealed record OverheadMonthResponse(DateOnly Month, IReadOnlyList<CurrencyTotalResponse> Totals);
