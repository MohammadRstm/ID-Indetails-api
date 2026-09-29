using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Response.Finance;

namespace ID_Indetails.api.Services.MoneySummaries.Abstraction;

/// <summary>A domain's money: the dashboard's money cards and the Financials page. A domain the caller can't see is not found.</summary>
public interface IDomainFinanceService
{
    Task<FinanceSummaryResponse> SummaryAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceMonthResponse>> MonthlyAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default);

    Task<FinanceProjectsResponse> ProjectsAsync(string domain, CancellationToken cancellationToken = default);

    Task<FinanceBillingResponse> BillingAsync(string domain, FinanceMonthsQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceClientResponse>> ClientsAsync(string domain, CancellationToken cancellationToken = default);

    Task<FinanceCostsResponse> CostsAsync(string domain, FinancePeriodQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MoneyAttentionResponse>> AttentionAsync(string domain, CancellationToken cancellationToken = default);
}
