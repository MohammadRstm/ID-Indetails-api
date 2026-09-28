using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Setup;
using ID_Indetails.api.DTO.Response.Setup;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Setup.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Developer-only setup. Requires the <c>X-Setup-Key</c> header to match the configured key;
/// otherwise every call answers 404 as if the endpoint did not exist.
/// </summary>
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
[RequireSetupKey]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class SetupController(ISetupService setupService) : ControllerBase
{
    /// <summary>Creates a super admin with a generated password, emailed to them.</summary>
    [HttpPost("super-admin")]
    [EnableRateLimiting(RateLimitPolicies.Setup)]
    public async Task<ActionResult<SuperAdminCreatedResponse>> CreateSuperAdmin(
        CreateSuperAdminRequest request,
        CancellationToken cancellationToken)
    {
        var created = await setupService.CreateSuperAdminAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}
