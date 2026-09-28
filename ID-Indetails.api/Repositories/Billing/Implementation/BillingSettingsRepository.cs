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

    public Task<bool> PrefixUsedElsewhereAsync(string prefix, string domainCode, CancellationToken cancellationToken = default)
        => db.BillingSettings.AnyAsync(s => s.InvoicePrefix == prefix && s.DomainCode != domainCode, cancellationToken);

    public void Add(BillingSettings settings) => db.BillingSettings.Add(settings);
}
