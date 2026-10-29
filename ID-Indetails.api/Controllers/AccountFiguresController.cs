using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>An Execution site's account. Its other money is on <c>GET /api/projects/{id}</c>.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/account-figures")]
public sealed class AccountFiguresController(IAccountFiguresService accountFigures) : ControllerBase
{
    /// <summary>What the client owes and has paid, what was spent, supervision taken, and the cash that should be in the project account.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<AccountFiguresResponse> Get(long projectId, CancellationToken cancellationToken)
        => accountFigures.GetAsync(projectId, cancellationToken);
}
