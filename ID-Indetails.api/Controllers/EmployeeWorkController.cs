using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Someone's Design work, for people who see everyone's. A person of a domain the caller can't see reads as 404.</summary>
[ApiController]
[Route("api/employees/{userId:long}")]
public sealed class EmployeeWorkController(IMyWorkService myWorkService, IWorkInsightsService insights, IMyTimeService myTime) : ControllerBase
{
    /// <summary>What their logged work cost the company: this quarter, all time and by project.</summary>
    [HttpGet("labour-cost")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<EmployeeLabourCostResponse> LabourCost(long userId, CancellationToken cancellationToken)
        => insights.PersonCostAsync(userId, cancellationToken);

    [HttpGet("work-summary")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<WorkSummaryResponse> Summary(long userId, [FromQuery] WorkSummaryQuery query, CancellationToken cancellationToken)
        => myWorkService.SummaryForPersonAsync(userId, query, cancellationToken);

    /// <summary>
    /// Their overtime in a month (this month by default), day by day, as they see it themselves. Which of it made up
    /// missing hours is only shown to callers who see salaries.
    /// </summary>
    [HttpGet("overtime")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<MyOvertimeResponse> Overtime(long userId, [FromQuery] SalaryMonthQuery query, CancellationToken cancellationToken)
        => myTime.OvertimeForPersonAsync(userId, query, cancellationToken);

    /// <summary>Their hours ledger, 12 months a page, as they see it themselves. It says what their pay is based on.</summary>
    [HttpGet("hours")]
    [HasPermission(PermissionCodes.SalaryView)]
    public Task<MyHoursResponse> Hours(long userId, [FromQuery] MyHoursQuery query, CancellationToken cancellationToken)
        => myTime.HoursForPersonAsync(userId, query, cancellationToken);
}
