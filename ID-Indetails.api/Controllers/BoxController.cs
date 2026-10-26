using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The Execution box: the pool of its projects' client payments, and the money moved to projects in the red.</summary>
[ApiController]
[Route("api/execution/box")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class BoxController(IBoxService box) : ControllerBase
{
    [HttpGet]
    public Task<BoxResponse> Get(CancellationToken cancellationToken)
        => box.GetAsync(cancellationToken);

    /// <summary>Every movement in and out, newest first.</summary>
    [HttpGet("entries")]
    public Task<PagedResponse<BoxEntryResponse>> Entries([FromQuery] BoxEntryListQuery query, CancellationToken cancellationToken)
        => box.EntriesAsync(query, cancellationToken);

    /// <summary>Moves money to a project in the red: at most the balance, and what brings the project back to zero.</summary>
    [HttpPost("transfers")]
    public Task<BoxResponse> Transfer(BoxTransferRequest request, CancellationToken cancellationToken)
        => box.TransferAsync(request, cancellationToken);

    /// <summary>Removes (<c>remove</c>) or keeps (<c>keep</c>) the money of a project closed by someone who can't see money.</summary>
    [HttpPost("projects/{projectId:long}/decision")]
    public Task<BoxResponse> Decide(long projectId, BoxDecisionRequest request, CancellationToken cancellationToken)
        => box.DecideAsync(projectId, request, cancellationToken);
}
