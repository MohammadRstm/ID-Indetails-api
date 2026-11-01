using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The caller's own working time. Never anyone else's: nothing here takes a user id. Their pay isn't shown to them.
/// </summary>
[ApiController]
[Route("api/me")]
[HasPermission(PermissionCodes.WorkEntryViewOwn)]
public sealed class MyTimeController(IMyTimeService myTime) : ControllerBase
{
    /// <summary>
    /// The caller's week, the company policy in force, and one day's hours (today by default): what logging on that
    /// day would count against.
    /// </summary>
    [HttpGet("schedule")]
    public Task<MyScheduleResponse> Schedule([FromQuery] MyScheduleQuery query, CancellationToken cancellationToken)
        => myTime.ScheduleAsync(query, cancellationToken);

    /// <summary>
    /// The overtime of work about to be logged or edited (leaving out the entry being edited, with the moves the form
    /// would make), and where it falls: the log form's overtime warning. Nothing is saved.
    /// </summary>
    [HttpPost("overtime-preview")]
    public Task<OvertimePreviewResponse> OvertimePreview(OvertimePreviewRequest request, CancellationToken cancellationToken)
        => myTime.PreviewOvertimeAsync(request, cancellationToken);

    /// <summary>The caller's hours ledger, 12 months a page from this month back to the one they joined.</summary>
    [HttpGet("hours")]
    public Task<MyHoursResponse> Hours([FromQuery] MyHoursQuery query, CancellationToken cancellationToken)
        => myTime.HoursAsync(query, cancellationToken);

    /// <summary>The caller's overtime in a month, day by day.</summary>
    [HttpGet("overtime")]
    public Task<MyOvertimeResponse> Overtime([FromQuery] SalaryMonthQuery query, CancellationToken cancellationToken)
        => myTime.OvertimeAsync(query, cancellationToken);
}
