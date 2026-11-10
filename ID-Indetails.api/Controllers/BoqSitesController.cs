using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Services.Boqs.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The sites a BOQ author writes drafts for, in either business: Execution's active client sites, by name and location.
/// Nothing else of a site is shown here.
/// </summary>
[ApiController]
[Route("api/boq-sites")]
[HasPermission(PermissionCodes.BoqCreate)]
public sealed class BoqSitesController(IBoqService boqs) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BoqSiteResponse>> List(CancellationToken cancellationToken) => boqs.SitesAsync(cancellationToken);

    [HttpGet("{projectId:long}")]
    public Task<BoqSiteResponse> Get(long projectId, CancellationToken cancellationToken) => boqs.SiteAsync(projectId, cancellationToken);
}
