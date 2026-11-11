using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.Services.Insights.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Financial Intelligence: derived judgements and simulations, kept apart from the Financials page's established
/// figures (<see cref="FinanceController"/>) and built only on the shared calculator. A project in a domain the caller
/// can't see is 404.
/// </summary>
[ApiController]
[Route("api/finance/insights")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class FinanceInsightsController(IFinanceInsightService insights) : ControllerBase
{
    /// <summary>One project's profitability, all time: agreed revenue, cost by source, profit, where it comes from and its standing.</summary>
    [HttpGet("projects/{id:long}")]
    public Task<ProjectInsightResponse> Project(long id, CancellationToken cancellationToken)
        => insights.ProjectAsync(id, cancellationToken);

    /// <summary>The project's figures today and with the changes asked for. Nothing is saved.</summary>
    [HttpGet("projects/{id:long}/scenario")]
    public Task<ProjectScenarioResponse> Scenario(long id, [FromQuery] ProjectScenarioQuery query, CancellationToken cancellationToken)
        => insights.ScenarioAsync(id, query, cancellationToken);
}
