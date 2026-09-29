using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own pay and working time. Never anyone else's: nothing here takes a user id.</summary>
[ApiController]
[Route("api/me")]
[HasPermission(PermissionCodes.WorkEntryViewOwn)]
public sealed class MyPayController(IMyTimeService myTime) : ControllerBase
{
    /// <summary>The caller's week, the company policy in force, and today's hours.</summary>
    [HttpGet("schedule")]
    public Task<MyScheduleResponse> Schedule(CancellationToken cancellationToken)
        => myTime.ScheduleAsync(cancellationToken);
}
