using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Response.Finance;

namespace ID_Indetails.api.Services.MoneySummaries.Abstraction;

/// <summary>A domain's money: the dashboard's money cards and the Financials page. A domain the caller can't see is not found.</summary>
public interface IDomainFinanceService
{
    Task<FinanceSummaryResponse> SummaryAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceMonthResponse>> MonthlyAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default);

    Task<FinanceProjectsResponse> ProjectsAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>The Financials export: every project's all-time money, then the period's totals, as CSV.</summary>
    Task<(string FileName, string Csv)> ExportProjectsAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default);

    Task<FinanceBillingResponse> BillingAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceClientResponse>> ClientsAsync(string domain, CancellationToken cancellationToken = default);

    Task<FinanceCostsResponse> CostsAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MoneyAttentionResponse>> AttentionAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// The summary's period money (received, cost by source, profit) for several periods at once, for the domain and
    /// for each of its projects, from one load: for comparing periods. Periods end today at the latest.
    /// </summary>
    Task<FinanceProjectPeriodsResponse> ProjectPeriodsAsync(
        string domain,
        IReadOnlyList<FinancePeriodQuery> periods,
        CancellationToken cancellationToken = default);
}
