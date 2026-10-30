using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Supervision the company took out of an Execution site's project account. Not a cost: it never changes profit.</summary>
[ApiController]
[Route("api")]
public sealed class SupervisionWithdrawalsController(ISupervisionWithdrawalService supervision) : ControllerBase
{
    /// <summary>The site's withdrawals, the latest day first.</summary>
    [HttpGet("projects/{projectId:long}/supervision-withdrawals")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<IReadOnlyList<SupervisionWithdrawalResponse>> List(long projectId, CancellationToken cancellationToken)
        => supervision.ListAsync(projectId, cancellationToken);

    [HttpPost("projects/{projectId:long}/supervision-withdrawals")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    public async Task<ActionResult<SupervisionWithdrawalResponse>> Create(long projectId, TakeSupervisionRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await supervision.TakeAsync(projectId, request, cancellationToken));

    /// <summary>Removes a withdrawal: it stays on record, marked removed.</summary>
    [HttpDelete("supervision-withdrawals/{withdrawalId:long}")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    public async Task<IActionResult> Delete(long withdrawalId, CancellationToken cancellationToken)
    {
        await supervision.RemoveAsync(withdrawalId, cancellationToken);
        return NoContent();
    }
}
