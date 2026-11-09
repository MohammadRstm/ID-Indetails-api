using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.TimeOff;
using ID_Indetails.api.DTO.Response.TimeOff;
using ID_Indetails.api.Services.TimeOff.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>A Design employee's own time off: asking for it and taking a request back while it waits.</summary>
[ApiController]
[Route("api/me/time-off")]
[HasPermission(PermissionCodes.TimeOffRequest)]
public sealed class MeTimeOffController(ITimeOffService timeOff) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TimeOffResponse>> Mine(CancellationToken cancellationToken) => timeOff.MineAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<TimeOffResponse>> Ask(AskTimeOffRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await timeOff.AskAsync(request, cancellationToken));

    [HttpPost("{id:long}/withdraw")]
    public Task<TimeOffResponse> Withdraw(long id, CancellationToken cancellationToken) => timeOff.WithdrawAsync(id, cancellationToken);
}
