using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Bills of quantities of Execution client projects: internal drafts, the client BOQ and variation orders. Agreeing
/// or withdrawing one sets the site's work price. Who may do what depends on the BOQ's kind, which the service checks;
/// a project the caller can't see reads as 404.
/// </summary>
[ApiController]
[Route("api")]
public sealed class BoqsController(IBoqService boqs, IDocumentService documents) : ControllerBase
{
    /// <summary>The project's BOQs: the caller's internal drafts, the client BOQs and VOs, and the price the agreed ones make.</summary>
    [HttpGet("projects/{projectId:long}/boqs")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.FinancialView, PermissionCodes.ProjectPricingManage)]
    public Task<BoqListResponse> List(long projectId, CancellationToken cancellationToken)
        => boqs.ListAsync(projectId, cancellationToken);

    /// <summary>Starts a BOQ, optionally with the lines of another. Client BOQs and VOs are numbered here.</summary>
    [HttpPost("projects/{projectId:long}/boqs")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.ProjectPricingManage)]
    public async Task<ActionResult<BoqDetailResponse>> Create(long projectId, CreateBoqRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await boqs.CreateAsync(projectId, request, cancellationToken));

    [HttpGet("boqs/{boqId:long}")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.FinancialView, PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> Get(long boqId, CancellationToken cancellationToken)
        => boqs.GetAsync(boqId, cancellationToken);

    /// <summary>Saves the whole document. Drafts stay editable after being sent; an agreed BOQ is locked.</summary>
    [HttpPut("boqs/{boqId:long}")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> Save(long boqId, SaveBoqRequest request, CancellationToken cancellationToken)
        => boqs.SaveAsync(boqId, request, cancellationToken);

    /// <summary>Appends lines of another BOQ of the project that the caller can see.</summary>
    [HttpPost("boqs/{boqId:long}/copy-lines")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> CopyLines(long boqId, CopyBoqLinesRequest request, CancellationToken cancellationToken)
        => boqs.CopyLinesAsync(boqId, request, cancellationToken);

    /// <summary>A new draft of the same kind with the same lines.</summary>
    [HttpPost("boqs/{boqId:long}/duplicate")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.ProjectPricingManage)]
    public async Task<ActionResult<BoqDetailResponse>> Duplicate(long boqId, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await boqs.DuplicateAsync(boqId, cancellationToken));

    /// <summary>Drafts only.</summary>
    [HttpDelete("boqs/{boqId:long}")]
    [HasAnyPermission(PermissionCodes.BoqCreate, PermissionCodes.ProjectPricingManage)]
    public async Task<IActionResult> Delete(long boqId, CancellationToken cancellationToken)
    {
        await boqs.DeleteAsync(boqId, cancellationToken);
        return NoContent();
    }

    /// <summary>Locks the BOQ and sets the work price. A price that would take the profit to zero is refused (<c>profit_floor</c>) and kept for forcing.</summary>
    [HttpPost("boqs/{boqId:long}/agree")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> Agree(long boqId, CancellationToken cancellationToken)
        => boqs.AgreeAsync(boqId, cancellationToken);

    /// <summary>Returns the BOQ to draft and takes it out of the work price, with a reason.</summary>
    [HttpPost("boqs/{boqId:long}/withdraw-agreement")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> WithdrawAgreement(long boqId, WithdrawBoqAgreementRequest request, CancellationToken cancellationToken)
        => boqs.WithdrawAgreementAsync(boqId, request, cancellationToken);

    /// <summary>Records a send (WhatsApp or a PDF download) with the BOQ's total as it is now. Repeatable.</summary>
    [HttpPost("boqs/{boqId:long}/deliveries")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    public Task<BoqDetailResponse> RecordDelivery(long boqId, RecordBoqDeliveryRequest request, CancellationToken cancellationToken)
        => boqs.RecordDeliveryAsync(boqId, request, cancellationToken);

    /// <summary>The client BOQ or VO as a PDF. Downloading it records nothing; record a download delivery for that.</summary>
    [HttpGet("boqs/{boqId:long}/pdf")]
    [HasAnyPermission(PermissionCodes.FinancialView, PermissionCodes.ProjectPricingManage)]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Pdf(long boqId, CancellationToken cancellationToken)
    {
        var pdf = await documents.BoqPdfAsync(boqId, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Emails the BOQ to the client with its PDF attached, and records the delivery.</summary>
    [HttpPost("boqs/{boqId:long}/email")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public Task<BoqDetailResponse> Email(long boqId, EmailBoqRequest request, CancellationToken cancellationToken)
        => documents.EmailBoqAsync(boqId, request, cancellationToken);
}
