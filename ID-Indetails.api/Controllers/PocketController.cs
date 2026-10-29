using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Execution's pocket money: money super admins set aside from sites, and paid back to sites that run short.</summary>
[ApiController]
[Route("api/execution/pocket")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class PocketController(IPocketService pocket) : ControllerBase
{
    [HttpGet]
    public Task<PocketResponse> Get(CancellationToken cancellationToken)
        => pocket.GetAsync(cancellationToken);

    /// <summary>Movements, newest first: <c>kind=contribution</c> for what came in, <c>kind=payout</c> for what went out.</summary>
    [HttpGet("entries")]
    public Task<PagedResponse<PocketEntryResponse>> Entries([FromQuery] PocketEntryListQuery query, CancellationToken cancellationToken)
        => pocket.EntriesAsync(query, cancellationToken);

    /// <summary>Moves part of a site's money into the pocket. Its profit must stay above zero.</summary>
    [HttpPost("contributions")]
    public Task<PocketResponse> Contribute(PocketContributionRequest request, CancellationToken cancellationToken)
        => pocket.ContributeAsync(request, cancellationToken);

    [HttpPost("requests")]
    public Task<PocketRequestResponse> Ask(CreatePocketRequestRequest request, CancellationToken cancellationToken)
        => pocket.RequestAsync(request, cancellationToken);

    /// <summary>Pays the chosen amount to the site, once; at most what the pocket holds.</summary>
    [HttpPost("requests/{requestId:long}/approve")]
    public Task<PocketRequestResponse> Approve(long requestId, ApprovePocketRequestRequest request, CancellationToken cancellationToken)
        => pocket.ApproveAsync(requestId, request, cancellationToken);

    [HttpPost("requests/{requestId:long}/deny")]
    public Task<PocketRequestResponse> Deny(long requestId, DenyPocketRequestRequest request, CancellationToken cancellationToken)
        => pocket.DenyAsync(requestId, request, cancellationToken);
}
