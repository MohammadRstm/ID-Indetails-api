using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Everyone's Design work by project and by person. Money only for callers with financial:view.</summary>
[ApiController]
[Route("api")]
public sealed class WorkInsightsController(IWorkInsightsService workInsightsService) : ControllerBase
{
    /// <summary>A project's logged work: totals, activity level, weekly hours, when work happens and its contributors.</summary>
    [HttpGet("projects/{projectId:long}/work")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<ProjectWorkResponse> ProjectWork(long projectId, [FromQuery] ProjectWorkQuery query, CancellationToken cancellationToken)
        => workInsightsService.ProjectWorkAsync(projectId, query, cancellationToken);

    /// <summary>One person's work on a project, narrowed like the contributors table.</summary>
    [HttpGet("projects/{projectId:long}/work/people/{userId:long}")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<ProjectContributorDetailResponse> Contributor(
        long projectId,
        long userId,
        [FromQuery] ProjectWorkQuery query,
        CancellationToken cancellationToken)
        => workInsightsService.ContributorAsync(projectId, userId, query, cancellationToken);

    /// <summary>A project's history day by day, with its status changes, progress and quiet gaps.</summary>
    [HttpGet("projects/{projectId:long}/timeline")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<ProjectTimelineResponse> Timeline(long projectId, CancellationToken cancellationToken)
        => workInsightsService.TimelineAsync(projectId, cancellationToken);

    /// <summary>Everyone of the Design domain with their hours, most this week first.</summary>
    [HttpGet("work/people")]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<IReadOnlyList<PersonWorkResponse>> People([FromQuery] PeopleWorkQuery query, CancellationToken cancellationToken)
        => workInsightsService.PeopleAsync(query, cancellationToken);
}
