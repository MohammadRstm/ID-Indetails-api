using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Changes refused because they would take a project's profit to zero or below (<c>profit_floor</c>): whoever sees the
/// project's money forces one through, once, or dismisses it. Hours someone logged on a day of their own time off
/// (<c>time_off_hours_held</c>) wait the same way for whoever decides time off (<c>time_off:decide</c>).
/// </summary>
[ApiController]
[Route("api/blocked-changes")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class BlockedChangesController(IBlockedChangeService changes) : ControllerBase
{
    [HttpGet("{id:long}")]
    public Task<BlockedChangeResponse> Get(long id, CancellationToken cancellationToken)
        => changes.GetAsync(id, cancellationToken);

    /// <summary>
    /// Makes the refused change, past the floor. 422 <c>blocked_change_decided</c> once it was forced or dismissed. Hours
    /// on a day of time off take <c>{ asOvertime }</c> (400 without it): approved as overtime or as normal hours.
    /// </summary>
    [HttpPost("{id:long}/force")]
    public Task<BlockedChangeResponse> Force(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ForceBlockedChangeRequest? request,
        CancellationToken cancellationToken)
        => changes.ForceAsync(id, request?.AsOvertime, cancellationToken);

    [HttpPost("{id:long}/dismiss")]
    public Task<BlockedChangeResponse> Dismiss(long id, CancellationToken cancellationToken)
        => changes.DismissAsync(id, cancellationToken);
}
