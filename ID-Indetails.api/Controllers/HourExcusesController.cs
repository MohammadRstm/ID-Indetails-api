using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Excused hours in someone's month. People outside the caller's domains read as 404.</summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.HoursExcuse)]
public sealed class HourExcusesController(IHourExcuseService excuses) : ControllerBase
{
    [HttpPost("employees/{userId:long}/hour-excuses")]
    public async Task<ActionResult<HourExcuseResponse>> Add(long userId, AddHourExcuseRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await excuses.AddAsync(userId, request, cancellationToken));

    [HttpDelete("hour-excuses/{excuseId:long}")]
    public async Task<IActionResult> Delete(long excuseId, CancellationToken cancellationToken)
    {
        await excuses.DeleteAsync(excuseId, cancellationToken);
        return NoContent();
    }
}
