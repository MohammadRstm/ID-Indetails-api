using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Design work entries. People log, edit and delete only their own.</summary>
[ApiController]
[Route("api/work-entries")]
public sealed class WorkEntriesController(
    IWorkEntryService workEntryService,
    IWorkEntryQueryService workEntryQueryService,
    IWorkEntryCommentService commentService) : ControllerBase
{
    /// <summary>Everyone's Design entries, newest first.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<PagedResponse<WorkEntryResponse>> List([FromQuery] WorkEntryListQuery query, CancellationToken cancellationToken)
        => workEntryQueryService.ListAsync(query, cancellationToken);

    /// <summary>An entry with its comments and the same person's other entries on the project that day.</summary>
    [HttpGet("{id:long}")]
    [HasAnyPermission(PermissionCodes.WorkEntryViewAll, PermissionCodes.WorkEntryViewOwn)]
    public Task<WorkEntryDetailResponse> Get(long id, CancellationToken cancellationToken)
        => workEntryQueryService.GetAsync(id, cancellationToken);

    [HttpGet("{id:long}/comments")]
    [HasAnyPermission(PermissionCodes.WorkEntryViewAll, PermissionCodes.WorkEntryViewOwn)]
    public Task<IReadOnlyList<WorkEntryCommentResponse>> ListComments(long id, CancellationToken cancellationToken)
        => commentService.ListAsync(id, cancellationToken);

    [HttpPost("{id:long}/comments")]
    [HasPermission(PermissionCodes.WorkEntryComment)]
    public async Task<ActionResult<WorkEntryCommentResponse>> AddComment(long id, AddWorkEntryCommentRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await commentService.AddAsync(id, request, cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.WorkEntryCreate)]
    public async Task<ActionResult<WorkEntryResponse>> Create(SaveWorkEntryRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await workEntryService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:long}")]
    [HasPermission(PermissionCodes.WorkEntryCreate)]
    public Task<WorkEntryResponse> Update(long id, SaveWorkEntryRequest request, CancellationToken cancellationToken)
        => workEntryService.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:long}")]
    [HasPermission(PermissionCodes.WorkEntryCreate)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await workEntryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Undoes a delete, within the edit window.</summary>
    [HttpPost("{id:long}/restore")]
    [HasPermission(PermissionCodes.WorkEntryCreate)]
    public Task<WorkEntryResponse> Restore(long id, CancellationToken cancellationToken)
        => workEntryService.RestoreAsync(id, cancellationToken);
}
