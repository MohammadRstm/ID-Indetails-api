using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Someone's Design work, for people who see everyone's. A person of a domain the caller can't see reads as 404.</summary>
[ApiController]
[Route("api/employees/{userId:long}")]
public sealed class EmployeeWorkController(IMyWorkService myWorkService) : ControllerBase
{
    [HttpGet("work-summary")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<WorkSummaryResponse> Summary(long userId, [FromQuery] WorkSummaryQuery query, CancellationToken cancellationToken)
        => myWorkService.SummaryForPersonAsync(userId, query, cancellationToken);
}
