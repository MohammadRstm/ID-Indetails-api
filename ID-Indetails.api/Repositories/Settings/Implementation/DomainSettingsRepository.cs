using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Settings.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Settings.Implementation;

public sealed class DomainSettingsRepository(IdIndetailsDbContext db) : IDomainSettingsRepository
{
    public Task<DomainSettings?> GetAsync(string domainCode, CancellationToken cancellationToken = default)
        => db.DomainSettings.AsNoTracking().SingleOrDefaultAsync(s => s.DomainCode == domainCode, cancellationToken);

    public Task<DomainSettings?> GetForUpdateAsync(string domainCode, CancellationToken cancellationToken = default)
        => db.DomainSettings
            .FromSql($"SELECT * FROM `domain_settings` WHERE `domain_code` = {domainCode} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(DomainSettings settings) => db.DomainSettings.Add(settings);
}
