using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Billing.Implementation;

public sealed class PaymentRepository(IdIndetailsDbContext db) : IPaymentRepository
{
    public async Task<(IReadOnlyList<Payment> Items, int TotalCount)> SearchAsync(
        PaymentSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Payments.AsNoTracking().Where(p => domains.Contains(p.Project.DomainCode));

        if (!search.IncludeVoided)
        {
            query = query.Where(p => p.VoidedAt == null);
        }

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(p => p.ProjectId == projectId);
        }

        if (search.ClientId is { } clientId)
        {
            query = query.Where(p => p.ClientId == clientId);
        }

        if (search.InvoiceId is { } invoiceId)
        {
            query = query.Where(p => p.InvoiceId == invoiceId);
        }

        if (search.Domain is { } domain)
        {
            query = query.Where(p => p.Project.DomainCode == domain);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.Project)
            .Include(p => p.Client)
            .Include(p => p.Invoice)
            .OrderByDescending(p => p.ReceivedOn)
            .ThenByDescending(p => p.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<Payment?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.Payments
            .AsNoTracking()
            .Include(p => p.Project)
            .Include(p => p.Client)
            .Include(p => p.Invoice)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payment?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => (await db.Payments
            .FromSql($"SELECT * FROM `payments` WHERE `id` = {id} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task<bool> HasLiveInitialAsync(long projectId, CancellationToken cancellationToken = default)
        => db.Payments.AnyAsync(p => p.ProjectId == projectId && p.IsInitial && p.VoidedAt == null, cancellationToken);

    public async Task<(decimal Received, decimal OnAccount)> ProjectTotalsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var totals = await db.Payments
            .Where(p => p.ProjectId == projectId && p.VoidedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Received = g.Sum(p => p.AmountUsd),
                OnAccount = g.Sum(p => p.InvoiceId == null ? p.AmountUsd : 0),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return totals is null ? (0, 0) : (totals.Received, totals.OnAccount);
    }

    public async Task<IReadOnlyList<ProjectBilling.IssuedInvoice>> IssuedInvoicesAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.Invoices
            .Where(i => i.ProjectId == projectId && i.IssuedAt != null && i.VoidedAt == null)
            .Select(i => new ProjectBilling.IssuedInvoice(
                i.Total,
                i.Payments.Where(p => p.VoidedAt == null).Sum(p => (decimal?)p.AmountUsd) ?? 0))
            .ToListAsync(cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}
