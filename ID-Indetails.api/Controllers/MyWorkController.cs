using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own Design work.</summary>
[ApiController]
[Route("api/me")]
public sealed class MyWorkController(IWorkEntryQueryService workEntryQueryService) : ControllerBase
{
    /// <summary>The caller's entries, newest first.</summary>
    [HttpGet("work-entries")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<PagedResponse<WorkEntryResponse>> ListEntries([FromQuery] MyWorkEntryListQuery query, CancellationToken cancellationToken)
        => workEntryQueryService.ListMineAsync(query, cancellationToken);
}
