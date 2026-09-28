using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Services.Statements.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Client statements of materials supplied on an Execution site. A statement is not an invoice and
/// changes nothing that is owed. Records of a domain the caller can't see read as 404.
/// </summary>
[ApiController]
[Route("api")]
public sealed class StatementsController(IStatementService statementService) : ControllerBase
{
    [HttpGet("projects/{projectId:long}/statements")]
    [HasAnyPermission(PermissionCodes.StatementCreate, PermissionCodes.StatementSend, PermissionCodes.FinancialView)]
    public Task<PagedResponse<StatementSummaryResponse>> List(long projectId, [FromQuery] StatementListQuery query, CancellationToken cancellationToken)
        => statementService.ListAsync(projectId, query, cancellationToken);

    /// <summary>The statement as it would be made now, with a provisional number. Nothing is saved.</summary>
    [HttpPost("projects/{projectId:long}/statements/preview")]
    [HasPermission(PermissionCodes.StatementCreate)]
    public Task<StatementResponse> Preview(long projectId, CreateStatementRequest request, CancellationToken cancellationToken)
        => statementService.PreviewAsync(projectId, request, cancellationToken);

    /// <summary>Numbers the statement and freezes the price of every material it shows.</summary>
    [HttpPost("projects/{projectId:long}/statements")]
    [HasPermission(PermissionCodes.StatementCreate)]
    public async Task<ActionResult<StatementResponse>> Create(long projectId, CreateStatementRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await statementService.CreateAsync(projectId, request, cancellationToken));

    [HttpGet("statements/{statementId:long}")]
    [HasAnyPermission(PermissionCodes.StatementCreate, PermissionCodes.StatementSend, PermissionCodes.FinancialView)]
    public Task<StatementResponse> Get(long statementId, CancellationToken cancellationToken)
        => statementService.GetAsync(statementId, cancellationToken);

    /// <summary>Records a send (email, WhatsApp or a PDF download). Repeatable.</summary>
    [HttpPost("statements/{statementId:long}/deliveries")]
    [HasPermission(PermissionCodes.StatementSend)]
    public Task<StatementResponse> RecordDelivery(long statementId, RecordDeliveryRequest request, CancellationToken cancellationToken)
        => statementService.RecordDeliveryAsync(statementId, request, cancellationToken);
}
