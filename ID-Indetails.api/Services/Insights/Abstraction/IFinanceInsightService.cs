using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;

namespace ID_Indetails.api.Services.Insights.Abstraction;

/// <summary>
/// Financial Intelligence: judgements and simulations built on the shared finance calculator, never a second one. Every
/// figure needs financial:view; a project in a domain the caller can't see is not found.
/// </summary>
public interface IFinanceInsightService
{
    Task<ProjectInsightResponse> ProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<ProjectScenarioResponse> ScenarioAsync(long projectId, ProjectScenarioQuery query, CancellationToken cancellationToken = default);
}
