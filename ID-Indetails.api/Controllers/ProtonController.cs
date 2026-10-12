using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Analysis;
using ID_Indetails.api.Proton.Memory;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Proton, the company assistant. Its answers only ever contain what the asker could see in the app themselves, and
/// it changes nothing unless the asker confirms a proposal: every tool runs under the authorization of its endpoint.
/// </summary>
[ApiController]
[Route("api/proton")]
public sealed class ProtonController(
    ProtonChatAgent agent,
    ProtonStreamingService streaming,
    ProtonActionService actions,
    ProtonUsageService usage,
    ProtonBriefingService briefing,
    ProtonMemoryService memories,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("messages")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    public Task<ProtonReplyResponse> Ask(AskProtonRequest request, CancellationToken cancellationToken)
        => agent.AskAsync(User, currentUser.Id, request, cancellationToken);

    /// <summary>
    /// The same question, answered live as Server-Sent Events (status, delta, then done or error). A refusal before the
    /// answer starts is an ordinary error response.
    /// </summary>
    [HttpPost("messages/stream")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    [Produces("text/event-stream")]
    public Task Stream(AskProtonRequest request, CancellationToken cancellationToken)
        => streaming.StreamAsync(HttpContext, currentUser.Id, request, cancellationToken);

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

    /// <summary>The caller's own proposals and what became of them (decision 37).</summary>
    [HttpGet("actions")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public Task<IReadOnlyList<ProtonActionSummaryResponse>> Actions(CancellationToken cancellationToken)
        => actions.MineAsync(currentUser.Id, cancellationToken);

    /// <summary>Everyone's proposals, with who they were for: the record of what Proton did, for those who manage it.</summary>
    [HttpGet("actions/all")]
    [HasPermission(PermissionCodes.ProtonBudgetManage)]
    public Task<IReadOnlyList<ProtonActionSummaryResponse>> AllActions(CancellationToken cancellationToken)
        => actions.AllAsync(cancellationToken);

    /// <summary>What Proton remembers for the caller between conversations, in their words.</summary>
    [HttpGet("memories")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public Task<IReadOnlyList<ProtonMemoryResponse>> Memories(CancellationToken cancellationToken)
        => memories.MineAsync(currentUser.Id, cancellationToken);

    /// <summary>Remembers something the caller typed in that conversation ("Remember this"): a preference, at most 20.</summary>
    [HttpPost("memories")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    public async Task<ActionResult<ProtonMemoryResponse>> Remember(SaveProtonMemoryRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await memories.SaveAsync(currentUser.Id, request, cancellationToken));

    [HttpDelete("memories/{id:long}")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public async Task<IActionResult> Forget(long id, CancellationToken cancellationToken)
    {
        await memories.ForgetAsync(currentUser.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    [HasPermission(PermissionCodes.ProtonUse)]
    public IActionResult EndConversation(Guid conversationId)
    {
        agent.Forget(currentUser.Id, conversationId);
        return NoContent();
    }

    /// <summary>
    /// The few things most worth a look, for Proton's greeting: what get_attention would find for the caller, read the
    /// same way. No model call, so it uses no question.
    /// </summary>
    [HttpGet("briefing")]
    [HasPermission(PermissionCodes.ProtonUse)]
    [EnableRateLimiting(RateLimitPolicies.Proton)]
    public Task<ProtonBriefingResponse> Briefing(CancellationToken cancellationToken)
        => briefing.BriefAsync(User, currentUser.Id, cancellationToken);

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
