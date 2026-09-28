using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Billing.Implementation;

public sealed class InvoiceRepository(IdIndetailsDbContext db) : IInvoiceRepository
{
    public async Task<(IReadOnlyList<InvoiceRow> Items, int TotalCount)> SearchAsync(
        InvoiceSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Invoices
            .AsNoTracking()
            .Where(i => domains.Contains(i.Project.DomainCode))
            .Select(i => new
            {
                Invoice = i,
                i.Project,
                i.Client,
                Paid = i.Payments.Where(p => p.VoidedAt == null).Sum(p => (decimal?)p.AmountUsd) ?? 0,
            });

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(r => r.Invoice.ProjectId == projectId);
        }

        if (search.ClientId is { } clientId)
        {
            query = query.Where(r => r.Invoice.ClientId == clientId);
        }

        if (search.Domain is { } domain)
        {
            query = query.Where(r => r.Project.DomainCode == domain);
        }

        var open = query.Where(r => r.Invoice.VoidedAt == null && r.Invoice.IssuedAt != null);
        query = search.Status switch
        {
            InvoiceStatus.Void => query.Where(r => r.Invoice.VoidedAt != null),
            InvoiceStatus.Draft => query.Where(r => r.Invoice.VoidedAt == null && r.Invoice.IssuedAt == null),
            InvoiceStatus.Paid => open.Where(r => r.Paid >= r.Invoice.Total),
            InvoiceStatus.Partial => open.Where(r => r.Paid > 0 && r.Paid < r.Invoice.Total),
            InvoiceStatus.Sent => open.Where(r => r.Paid == 0 && r.Invoice.Total > 0),
            _ => query,
        };

        if (search.Overdue == true)
        {
            // Due dates are stored at noon UTC of the due day (see InvoiceService).
            var todayNoon = search.Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            query = query.Where(r => r.Invoice.VoidedAt == null
                && r.Invoice.IssuedAt != null
                && r.Paid < r.Invoice.Total
                && r.Invoice.DueAt < todayNoon);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(r => r.Invoice.CreatedAt)
            .ThenByDescending(r => r.Invoice.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(r => Row(r.Invoice, r.Project, r.Client, r.Paid)).ToList(), total);
    }

    public async Task<InvoiceRow?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var row = await db.Invoices
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new
            {
                Invoice = i,
                i.Project,
                i.Client,
                Lines = i.Lines.OrderBy(l => l.Position).ToList(),
                Deliveries = i.Deliveries.OrderBy(d => d.SentAt).ToList(),
                Paid = i.Payments.Where(p => p.VoidedAt == null).Sum(p => (decimal?)p.AmountUsd) ?? 0,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        row.Invoice.Lines = row.Lines;
        row.Invoice.Deliveries = row.Deliveries;
        return Row(row.Invoice, row.Project, row.Client, row.Paid);
    }

    public async Task<Invoice?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        var invoice = (await db.Invoices
            .FromSql($"SELECT * FROM `invoices` WHERE `id` = {id} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

        if (invoice is not null)
        {
            await db.Entry(invoice).Collection(i => i.Lines).LoadAsync(cancellationToken);
        }

        return invoice;
    }

    public async Task<Invoice?> GetForProjectForUpdateAsync(long projectId, long id, CancellationToken cancellationToken = default)
        => (await db.Invoices
            .FromSql($"SELECT * FROM `invoices` WHERE `id` = {id} AND `project_id` = {projectId} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    // A plain SELECT would read the transaction's snapshot, which can predate a payment committed
    // while this transaction waited for the invoice lock.
    public async Task<decimal> PaidForUpdateAsync(long invoiceId, CancellationToken cancellationToken = default)
        => (await db.Database
            .SqlQuery<decimal>($"SELECT COALESCE(SUM(`amount_usd`), 0) AS `Value` FROM `payments` WHERE `invoice_id` = {invoiceId} AND `voided_at` IS NULL FOR UPDATE")
            .ToListAsync(cancellationToken))
            .Single();

    public async Task<int> NextSequenceAsync(string domainCode, int year, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `invoice_sequences` (`domain_code`, `year`, `last_number`) VALUES ({domainCode}, {year}, 1) ON DUPLICATE KEY UPDATE `last_number` = `last_number` + 1",
            cancellationToken);

        return await db.Database
            .SqlQuery<int>($"SELECT `last_number` AS `Value` FROM `invoice_sequences` WHERE `domain_code` = {domainCode} AND `year` = {year}")
            .SingleAsync(cancellationToken);
    }

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    public void AddDelivery(InvoiceDelivery delivery) => db.InvoiceDeliveries.Add(delivery);

    public void Remove(Invoice invoice) => db.Invoices.Remove(invoice);

    private static InvoiceRow Row(Invoice invoice, Project project, Client client, decimal paid)
    {
        invoice.Project = project;
        invoice.Client = client;
        return new InvoiceRow(invoice, paid);
    }
}
