using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The signed-in client. Only portal tokens pass; staff tokens are refused.</summary>
[ApiController]
[Route("api/portal")]
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalController(IPortalAuthService portalAuth, ICurrentPortalAccount currentAccount) : ControllerBase
{
    /// <summary>The client's account and the Design projects they may open.</summary>
    [HttpGet("me")]
    public Task<PortalAccountResponse> Me(CancellationToken cancellationToken)
        => portalAuth.GetAccountAsync(currentAccount.Id, cancellationToken);
}
