using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;

namespace ID_Indetails.api.Services.Insights.Abstraction;

/// <summary>
/// Financial Intelligence across a domain (Design or Execution): its projects' risks and its spending's trends and
/// anomalies, from the shared calculator and the owner's thresholds (Proton:Insights). Every figure needs
/// financial:view; a domain the caller can't see is not found.
/// </summary>
public interface IDomainInsightService
{
    Task<ProjectRisksResponse> RisksAsync(string domain, CancellationToken cancellationToken = default);

    Task<ExpenseTrendsResponse> ExpensesAsync(string domain, ExpenseTrendsQuery query, CancellationToken cancellationToken = default);
}
