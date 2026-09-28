using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>An Execution site's money over time. The rest of its money is on <c>GET /api/projects/{id}</c>.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/site-money")]
public sealed class SiteMoneyController(ISiteMoneyService siteMoney) : ControllerBase
{
    /// <summary>Cash week by week, and when profit would reach zero at today's crew and day rate.</summary>
    [HttpGet("timeline")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<SiteMoneyTimelineResponse> Timeline(long projectId, CancellationToken cancellationToken)
        => siteMoney.TimelineAsync(projectId, cancellationToken);
}
