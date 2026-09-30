using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>A domain's money, in USD: the dashboard money cards and the Financials page. A domain the caller can't see is 404.</summary>
[ApiController]
[Route("api/finance/{domain}")]
[HasPermission(PermissionCodes.FinancialView)]
public sealed class FinanceController(IDomainFinanceService financeService) : ControllerBase
{
    /// <summary>Revenue, cost, profit and margin over a period, with the billing position as of today.</summary>
    [HttpGet("summary")]
    public Task<FinanceSummaryResponse> Summary(string domain, [FromQuery] FinancePeriodQuery query, CancellationToken cancellationToken)
        => financeService.SummaryAsync(domain, query, cancellationToken);

    /// <summary>Revenue against cost, month by month, oldest first.</summary>
    [HttpGet("monthly")]
    public Task<IReadOnlyList<FinanceMonthResponse>> Monthly(string domain, [FromQuery] FinanceMonthsQuery query, CancellationToken cancellationToken)
        => financeService.MonthlyAsync(domain, query, cancellationToken);

    /// <summary>Every project's all-time money, most profitable first, with totals.</summary>
    [HttpGet("projects")]
    public Task<FinanceProjectsResponse> Projects(string domain, CancellationToken cancellationToken)
        => financeService.ProjectsAsync(domain, cancellationToken);

    /// <summary>The Financials export: every project's all-time money, then the period's totals (UTF-8 CSV).</summary>
    [HttpGet("projects/export")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> ExportProjects(string domain, [FromQuery] FinancePeriodQuery query, CancellationToken cancellationToken)
    {
        var (fileName, csv) = await financeService.ExportProjectsAsync(domain, query, cancellationToken);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
    }

    /// <summary>Billing and collections: the position, aging, invoiced against collected, clients, methods and open invoices.</summary>
    [HttpGet("billing")]
    public Task<FinanceBillingResponse> Billing(string domain, [FromQuery] FinanceMonthsQuery query, CancellationToken cancellationToken)
        => financeService.BillingAsync(domain, query, cancellationToken);

    /// <summary>Every client with projects, with their money across them, most revenue first.</summary>
    [HttpGet("clients")]
    public Task<IReadOnlyList<FinanceClientResponse>> Clients(string domain, CancellationToken cancellationToken)
        => financeService.ClientsAsync(domain, cancellationToken);

    /// <summary>Costs over a period: by source, labour by person, expenses by category and the biggest expenses.</summary>
    [HttpGet("costs")]
    public Task<FinanceCostsResponse> Costs(string domain, [FromQuery] FinancePeriodQuery query, CancellationToken cancellationToken)
        => financeService.CostsAsync(domain, query, cancellationToken);

    /// <summary>The dashboard's money follow-ups, 6 at most: overdue invoices, finished but owed, no payment, cost past 85% of the payment.</summary>
    [HttpGet("attention")]
    public Task<IReadOnlyList<MoneyAttentionResponse>> Attention(string domain, CancellationToken cancellationToken)
        => financeService.AttentionAsync(domain, cancellationToken);
}
