using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.MoneySummaries.Implementation;

public sealed class MoneySummaryRepository(IdIndetailsDbContext db) : IMoneySummaryRepository
{
    public async Task<IReadOnlyList<Project>> DomainProjectsAsync(string domainCode, CancellationToken cancellationToken = default)
        => await db.Projects.AsNoTracking().Include(p => p.Client).Where(p => p.DomainCode == domainCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Project>> ClientProjectsAsync(IReadOnlyCollection<long> clientIds, CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.ClientId != null && clientIds.Contains(p.ClientId.Value))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Client>> DomainClientsAsync(string domainCode, CancellationToken cancellationToken = default)
        => await db.Clients.AsNoTracking().Where(c => c.DomainCode == domainCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoiceMoneyRow>> InvoicesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.Invoices
            .AsNoTracking()
            .Where(i => projectIds.Contains(i.ProjectId))
            .Select(i => new InvoiceMoneyRow(
                i.Id,
                i.ProjectId,
                i.ClientId,
                i.Number,
                i.Currency,
                i.Total,
                i.IssuedAt,
                i.DueAt,
                i.VoidedAt != null,
                db.Payments.Where(p => p.InvoiceId == i.Id && p.VoidedAt == null).Sum(p => (decimal?)p.AmountUsd) ?? 0))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentMoneyRow>> PaymentsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.Payments
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.ProjectId) && p.VoidedAt == null)
            .Select(p => new PaymentMoneyRow(p.ProjectId, p.ClientId, p.InvoiceId, p.ReceivedOn, p.AmountUsd, p.Method))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpenseDetailRow>> ExpensesAsync(
        IReadOnlyCollection<long> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => await db.ProjectExpenses
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId) && x.DeletedAt == null && x.SpentOn >= from && x.SpentOn <= to)
            .Select(x => new ExpenseDetailRow(x.Id, x.ProjectId, x.Label, x.Category, x.Currency, x.Amount, x.SpentOn))
            .ToListAsync(cancellationToken);
}
