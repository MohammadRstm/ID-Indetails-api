using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class CompanyFinanceRepository(IdIndetailsDbContext db) : ICompanyFinanceRepository
{
    public async Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken cancellationToken = default)
        => await db.Projects.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> EmployeesAsync(CancellationToken cancellationToken = default)
        => await db.Users
            .Where(u => u.RoleCode == RoleCodes.Employee && u.Status == UserStatus.Active && u.DomainCode != null)
            .GroupBy(u => u.DomainCode!)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> ClientsAsync(CancellationToken cancellationToken = default)
        => await db.Clients.GroupBy(c => c.DomainCode).ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

    public async Task<IReadOnlyList<IssuedInvoiceRow>> IssuedInvoicesAsync(CancellationToken cancellationToken = default)
        => await db.Invoices
            .AsNoTracking()
            .Where(i => i.IssuedAt != null && i.VoidedAt == null)
            .Select(i => new IssuedInvoiceRow(
                i.Project.DomainCode,
                i.IssuedAt!.Value,
                i.Total,
                i.Currency,
                db.Payments.Where(p => p.InvoiceId == i.Id && p.VoidedAt == null).Sum(p => (decimal?)p.AmountUsd) ?? 0))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReceivedPaymentRow>> PaymentsAsync(CancellationToken cancellationToken = default)
        => await db.Payments
            .AsNoTracking()
            .Where(p => p.VoidedAt == null)
            .Select(p => new ReceivedPaymentRow(p.Project.DomainCode, p.ReceivedOn, p.AmountUsd))
            .ToListAsync(cancellationToken);
}
