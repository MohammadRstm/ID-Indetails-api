using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Statements;

namespace ID_Indetails.api.Services.Statements.Abstraction;

/// <summary>Client statements of Execution sites in the caller's visible domains.</summary>
public interface IStatementService
{
    Task<PagedResponse<StatementSummaryResponse>> ListAsync(long projectId, StatementListQuery query, CancellationToken cancellationToken = default);

    Task<StatementResponse> GetAsync(long statementId, CancellationToken cancellationToken = default);

    /// <summary>The statement as it would be made now, saving nothing.</summary>
    Task<StatementResponse> PreviewAsync(long projectId, CreateStatementRequest request, CancellationToken cancellationToken = default);

    Task<StatementResponse> CreateAsync(long projectId, CreateStatementRequest request, CancellationToken cancellationToken = default);

    Task<StatementResponse> RecordDeliveryAsync(long statementId, RecordDeliveryRequest request, CancellationToken cancellationToken = default);
}
