using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Variation orders of Design client projects: lines that change the price, applied at once or sent to the client to
/// confirm, each divided between the project's departments. Execution sites have BOQ variation orders instead.
/// </summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class DesignVosController(IDesignVoService vos, IDocumentService documents) : ControllerBase
{
    /// <summary>The project's VOs, newest first.</summary>
    [HttpGet("projects/{projectId:long}/design-vos")]
    public Task<IReadOnlyList<DesignVoResponse>> List(long projectId, CancellationToken cancellationToken)
        => vos.ListAsync(projectId, cancellationToken);

    /// <summary>The hours logged outside the agreed scope since the last applied VO, or the project's start.</summary>
    [HttpGet("projects/{projectId:long}/design-vos/outside-scope")]
    public Task<OutsideScopeResponse> OutsideScope(long projectId, CancellationToken cancellationToken)
        => vos.OutsideScopeAsync(projectId, cancellationToken);

    /// <summary>
    /// Makes a VO, numbered <c>VO-YYYY-NNN</c>: applied at once (<c>apply</c>), or pending until confirmed. Applying one
    /// that lowers the price may be refused by the profit floor (422 <c>profit_floor</c>): the VO stays pending.
    /// </summary>
    [HttpPost("projects/{projectId:long}/design-vos")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public async Task<IActionResult> Create(long projectId, CreateDesignVoRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await vos.CreateAsync(projectId, request, cancellationToken));

    /// <summary>The client agreed: the pending VO joins the price. 422 <c>design_vo_not_pending</c> otherwise.</summary>
    [HttpPost("design-vos/{voId:long}/confirm")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<DesignVoResponse> Confirm(long voId, CancellationToken cancellationToken)
        => vos.ConfirmAsync(voId, cancellationToken);

    /// <summary>
    /// Adds the project's TVA to a VO, or takes it off: the superadmin's call, on a pending or applied VO. An applied one
    /// changes what the client pays in all. 422 <c>design_vo_not_pending</c> on a cancelled one.
    /// </summary>
    [HttpPut("design-vos/{voId:long}/tva")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<DesignVoResponse> SetTva(long voId, SetDesignVoTvaRequest request, CancellationToken cancellationToken)
        => vos.SetTvaAsync(voId, request, cancellationToken);

    /// <summary>Takes a pending VO back; its number isn't used again.</summary>
    [HttpPost("design-vos/{voId:long}/cancel")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<DesignVoResponse> Cancel(long voId, CancellationToken cancellationToken)
        => vos.CancelAsync(voId, cancellationToken);

    [HttpGet("design-vos/{voId:long}/pdf")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Pdf(long voId, CancellationToken cancellationToken)
    {
        var pdf = await documents.DesignVoPdfAsync(voId, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Emails the VO to the client with its PDF attached.</summary>
    [HttpPost("design-vos/{voId:long}/email")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public async Task<IActionResult> Email(long voId, EmailDocumentRequest request, CancellationToken cancellationToken)
    {
        await documents.EmailDesignVoAsync(voId, request, cancellationToken);
        return NoContent();
    }
}
