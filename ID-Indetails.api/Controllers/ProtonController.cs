using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Proton, the company assistant. Its answers only ever contain what the asker could see in Hourline themselves, and
/// it changes nothing unless the asker confirms a proposal: every tool runs under the authorization of its endpoint.
/// </summary>
[ApiController]
[Route("api/proton")]
public sealed class ProtonController(
    ProtonChatAgent agent,
    ProtonActionService actions,
    ProtonUsageService usage,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("messages")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    public Task<ProtonReplyResponse> Ask(AskProtonRequest request, CancellationToken cancellationToken)
        => agent.AskAsync(User, currentUser.Id, request, cancellationToken);

    /// <summary>
    /// Carries out a change Proton proposed, once. Only the proposer, in its conversation, before it expires — and only
    /// if they still pass the permissions of the endpoint that makes the change, which are checked again here.
    /// </summary>
    [HttpPost("actions/{actionId:guid}/confirm")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    public Task<ProtonActionOutcomeResponse> Confirm(Guid actionId, DecideProtonActionRequest request, CancellationToken cancellationToken)
        => actions.ConfirmAsync(User, currentUser.Id, actionId, request, cancellationToken);

    [HttpPost("actions/{actionId:guid}/cancel")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public Task<ProtonActionOutcomeResponse> Cancel(Guid actionId, DecideProtonActionRequest request, CancellationToken cancellationToken)
        => actions.CancelAsync(currentUser.Id, actionId, request, cancellationToken);

    [HttpDelete("conversations/{conversationId:guid}")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public IActionResult EndConversation(Guid conversationId)
    {
        agent.Forget(currentUser.Id, conversationId);
        return NoContent();
    }

    /// <summary>The caller's question allowance this month. Never money.</summary>
    [HttpGet("usage")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public Task<ProtonUsageResponse> Usage(CancellationToken cancellationToken)
        => usage.MineAsync(currentUser.Id, cancellationToken);

    [HttpGet("budget")]
    [HasPermission(PermissionCodes.ProtonBudgetManage)]
    public Task<ProtonBudgetResponse> Budget(CancellationToken cancellationToken)
        => usage.BudgetAsync(cancellationToken);

    [HttpPost("budget/top-ups")]
    [HasPermission(PermissionCodes.ProtonBudgetManage)]
    public async Task<ActionResult<ProtonBudgetResponse>> TopUp(TopUpProtonBudgetRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await usage.TopUpAsync(currentUser.Id, request, cancellationToken));
}
