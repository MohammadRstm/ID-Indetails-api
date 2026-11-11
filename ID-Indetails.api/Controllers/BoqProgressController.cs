using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Services.Boqs.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// An Execution site's progress by value: executed quantities on the lines of its agreed client BOQ and VOs, recorded by
/// its own staff (as for daily logs) or by a pricing manager, and the value executed against the agreed value. The service
/// decides who reaches which site; a project the caller can't see reads as 404, a Design project is 422
/// <c>not_for_this_domain</c>.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/boqs/progress")]
public sealed class BoqProgressController(IBoqProgressService progress) : ControllerBase
{
    /// <summary>
    /// Each line with its quantity, latest executed quantity and progress, the site's progress by value, and the latest
    /// records. Prices and values only with <c>financial:view</c>.
    /// </summary>
    [HttpGet]
    [HasAnyPermission(PermissionCodes.SiteLogCreate, PermissionCodes.SiteLogViewAll, PermissionCodes.FinancialView, PermissionCodes.ProjectPricingManage)]
    public Task<BoqProgressResponse> Get(long projectId, [FromQuery] BoqProgressQuery query, CancellationToken cancellationToken)
        => progress.GetAsync(projectId, query, cancellationToken);

    /// <summary>Records executed quantities on a day (today when left out); a line already recorded that day is replaced.</summary>
    [HttpPut]
    [HasAnyPermission(PermissionCodes.SiteLogCreate, PermissionCodes.ProjectPricingManage)]
    public Task<BoqProgressResponse> Record(long projectId, RecordBoqProgressRequest request, CancellationToken cancellationToken)
        => progress.RecordAsync(projectId, request, cancellationToken);
}
