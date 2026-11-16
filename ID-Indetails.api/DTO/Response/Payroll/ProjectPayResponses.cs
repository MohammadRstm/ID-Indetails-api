using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Expenses;

namespace ID_Indetails.api.DTO.Response.Payroll;

/// <param name="RateChanged">Their hourly rate changed during the period.</param>
/// <param name="Priced">False when they had no pay record (their time is paid 0).</param>
/// <param name="MonthlySalary">The salary behind their hourly rate on their latest day of work in the period; null without a pay record.</param>
public sealed record ProjectPayPersonResponse(
    long UserId,
    string Name,
    string Email,
    TeamSummaryResponse? Team,
    int Entries,
    int NormalMinutes,
    int OvertimeMinutes,
    decimal HourlyRate,
    bool RateChanged,
    string Currency,
    decimal Pay,
    bool Priced,
    long? PhotoFileId,
    string? Position,
    decimal? MonthlySalary);

/// <param name="Code">The project's full code, ID26Q4001-VK.</param>
public sealed record ProjectPayProjectResponse(
    long ProjectId,
    string Title,
    string Code,
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
