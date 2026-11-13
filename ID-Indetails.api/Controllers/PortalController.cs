using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Support.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>The signed-in client. Only portal tokens pass; staff tokens are refused.</summary>
[ApiController]
[Route("api/portal")]
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalController(IPortalAuthService portalAuth, ICurrentPortalAccount currentAccount, IBugReportService messages) : ControllerBase
{
    /// <summary>The client's account and the Design projects they may open.</summary>
    [HttpGet("me")]
    public Task<PortalAccountResponse> Me(CancellationToken cancellationToken)
        => portalAuth.GetAccountAsync(currentAccount.Id, cancellationToken);

    /// <summary>A message from the client to the developer, emailed as staff members' are ("Contact the developer").</summary>
    [HttpPost("contact")]
    [EnableRateLimiting(RateLimitPolicies.BugReport)]
    public async Task<IActionResult> Contact(ReportBugRequest request, CancellationToken cancellationToken)
    {
        await messages.SendFromClientAsync(request, Request.Headers.UserAgent.ToString(), cancellationToken);
        return NoContent();
    }
}
