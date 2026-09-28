using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Billing.Implementation;

public sealed class BillingSettingsRepository(IdIndetailsDbContext db) : IBillingSettingsRepository
{
    public Task<BillingSettings?> GetAsync(string domainCode, CancellationToken cancellationToken = default)
        => db.BillingSettings.AsNoTracking().SingleOrDefaultAsync(s => s.DomainCode == domainCode, cancellationToken);

    public Task<BillingSettings?> GetForUpdateAsync(string domainCode, CancellationToken cancellationToken = default)
        => db.BillingSettings.SingleOrDefaultAsync(s => s.DomainCode == domainCode, cancellationToken);

    public async Task<bool> PrefixUsedElsewhereAsync(string prefix, string domainCode, CancellationToken cancellationToken = default)
    {
        var numberStart = prefix + "-";
        return await db.BillingSettings.AnyAsync(s => s.InvoicePrefix == prefix && s.DomainCode != domainCode, cancellationToken)
            || await db.Invoices.AnyAsync(
                i => i.Number != null && i.Number.StartsWith(numberStart) && i.Project.DomainCode != domainCode,
                cancellationToken);
    }

    // The domain rows always exist, unlike a domain's first billing-settings row.
    public Task LockAllAsync(CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `code` FROM `domains` FOR UPDATE", cancellationToken);

    public void Add(BillingSettings settings) => db.BillingSettings.Add(settings);
}
