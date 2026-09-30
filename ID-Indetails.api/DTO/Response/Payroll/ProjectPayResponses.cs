using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Payroll;

/// <param name="Basis">Pay basis on their latest day of work in the period; null without a pay record.</param>
/// <param name="RateChanged">Their hourly rate changed during the period.</param>
/// <param name="Priced">False when they had no pay record (their time is paid 0).</param>
public sealed record ProjectPayPersonResponse(
    long UserId,
    string Name,
    string Email,
    TeamSummaryResponse? Team,
    PayBasis? Basis,
    int Entries,
    int NormalMinutes,
    int OvertimeMinutes,
    decimal HourlyRate,
    bool RateChanged,
    string Currency,
    decimal Pay,
    bool Priced,
    long? PhotoFileId);

public sealed record ProjectPayProjectResponse(
    long ProjectId,
    string Title,
    string? Client,
    int NormalMinutes,
    int OvertimeMinutes,
    IReadOnlyList<CurrencyTotalResponse> Pay,
    IReadOnlyList<ProjectPayPersonResponse> People);

/// <param name="To">Last day of the period.</param>
/// <param name="DesignProjects">All Design projects, with or without work in the period.</param>
public sealed record ProjectPayResponse(
    ProjectPayUnit Unit,
    DateOnly From,
    DateOnly To,
    int DesignProjects,
    IReadOnlyList<ProjectPayProjectResponse> Projects,
    IReadOnlyList<CurrencyTotalResponse> Totals,
    int People,
    int NormalMinutes,
    int OvertimeMinutes);
