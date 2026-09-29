using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.Requirements.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own Design work.</summary>
[ApiController]
[Route("api/me")]
public sealed class MyWorkController(
    IWorkEntryQueryService workEntryQueryService,
    IMyWorkService myWorkService,
    IRequirementService requirementService) : ControllerBase
{
    /// <summary>The caller's entries, newest first.</summary>
    [HttpGet("work-entries")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<PagedResponse<WorkEntryResponse>> ListEntries([FromQuery] MyWorkEntryListQuery query, CancellationToken cancellationToken)
        => workEntryQueryService.ListMineAsync(query, cancellationToken);

    /// <summary>The caller's totals, weekly series, active days and time per project.</summary>
    [HttpGet("work-summary")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<WorkSummaryResponse> Summary([FromQuery] WorkSummaryQuery query, CancellationToken cancellationToken)
        => myWorkService.SummaryForMeAsync(query, cancellationToken);

    /// <summary>Every open project of the domain, with the caller's own time on it.</summary>
    [HttpGet("projects")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<IReadOnlyList<MyProjectResponse>> Projects(CancellationToken cancellationToken)
        => myWorkService.MyProjectsAsync(cancellationToken);

    /// <summary>The caller's work on one project, and the team's latest entries there.</summary>
    [HttpGet("projects/{projectId:long}/work")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<MyProjectWorkResponse> ProjectWork(long projectId, CancellationToken cancellationToken)
        => myWorkService.MyProjectWorkAsync(projectId, cancellationToken);

    /// <summary>
    /// The caller's open requirements on active and on-hold projects, soonest deadline first. In both domains: it is
    /// the whole of an Execution employee's My work page.
    /// </summary>
    [HttpGet("requirements")]
    [HasPermission(PermissionCodes.RequirementView)]
    public Task<MyRequirementsResponse> Requirements(CancellationToken cancellationToken)
        => requirementService.ListMineAsync(cancellationToken);

    /// <summary>The caller's entries and the domain's meetings on one day (the day timeline). Defaults to today.</summary>
    [HttpGet("day")]
    [HasPermission(PermissionCodes.WorkEntryViewOwn)]
    public Task<MyDayResponse> Day([FromQuery] DateOnly? date, CancellationToken cancellationToken)
        => myWorkService.MyDayAsync(date, cancellationToken);
}
