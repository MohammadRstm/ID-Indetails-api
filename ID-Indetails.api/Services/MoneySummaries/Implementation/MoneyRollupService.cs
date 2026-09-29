using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;

namespace ID_Indetails.api.Services.MoneySummaries.Implementation;

/// <summary>
/// The shared finance calculator (one query per cost source) plus invoices, payments and exchange rates: the same
/// number of queries for one project or a thousand.
/// </summary>
public sealed class MoneyRollupService(
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IMoneySummaryRepository summaries,
    IClock clock) : IMoneyRollupService
{
    public async Task<MoneyRollup> LoadAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken = default)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var today = clock.CompanyToday;
        var history = ids.Count > 0 ? await finance.CostHistoryAsync(projects, cancellationToken) : new Dictionary<long, ProjectCostHistory>();
        var rows = ids.Count > 0 ? await summaries.InvoicesAsync(ids, cancellationToken) : [];
        var payments = ids.Count > 0 ? await summaries.PaymentsAsync(ids, cancellationToken) : [];
        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));

        var invoices = new List<ProjectBilling.Invoice>();
        var unconverted = new List<InvoiceMoneyRow>();
        foreach (var row in rows)
        {
            // An invoice is converted at the rate of the day it was issued; a draft at today's.
            var day = row.IssuedAt is { } issued ? clock.ToCompanyDate(issued) : today;
            if (rates.ToUsd(row.Total, row.Currency, day) is not { } total)
            {
                unconverted.Add(row);
                continue;
            }

            invoices.Add(new ProjectBilling.Invoice(
                row.Id,
                row.ProjectId,
                row.ClientId,
                row.IssuedAt is not null,
                row.IsVoided,
                row.DueAt is { } due ? DateOnly.FromDateTime(due) : null,
                total,
                row.PaidUsd));
        }

        return new MoneyRollup(
            history,
            invoices,
            rows.ToDictionary(r => r.Id),
            unconverted,
            payments.Select(p => new ProjectBilling.Payment(p.ProjectId, p.ClientId, p.InvoiceId, p.ReceivedOn, p.AmountUsd, p.Method)).ToList(),
            today);
    }
}
