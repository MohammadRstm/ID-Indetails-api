using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Changes refused because they would take a project's profit to zero or below (<c>profit_floor</c>): whoever sees the
/// project's money forces one through, once, or dismisses it.
/// </summary>
[ApiController]
[Route("api/blocked-changes")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class BlockedChangesController(IBlockedChangeService changes) : ControllerBase
{
    [HttpGet("{id:long}")]
    public Task<BlockedChangeResponse> Get(long id, CancellationToken cancellationToken)
        => changes.GetAsync(id, cancellationToken);

    /// <summary>Makes the refused change, past the floor. 422 <c>blocked_change_decided</c> once it was forced or dismissed.</summary>
    [HttpPost("{id:long}/force")]
    public Task<BlockedChangeResponse> Force(long id, CancellationToken cancellationToken)
        => changes.ForceAsync(id, cancellationToken);

    [HttpPost("{id:long}/dismiss")]
    public Task<BlockedChangeResponse> Dismiss(long id, CancellationToken cancellationToken)
        => changes.DismissAsync(id, cancellationToken);
}
