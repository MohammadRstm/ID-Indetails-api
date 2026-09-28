using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class CompanyRepository(IdIndetailsDbContext db) : ICompanyRepository
{
    public Task<CompanySettings?> GetSettingsAsync(bool forUpdate = false, CancellationToken cancellationToken = default)
        => (forUpdate ? db.CompanySettings : db.CompanySettings.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);

    public void AddSettings(CompanySettings settings) => db.CompanySettings.Add(settings);

    public async Task<IReadOnlyList<WorkingTimePolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default)
        => await db.WorkingTimePolicies.AsNoTracking().OrderBy(p => p.EffectiveFrom).ToListAsync(cancellationToken);

    public Task<WorkingTimePolicy?> GetPolicyForUpdateAsync(DateOnly effectiveFrom, CancellationToken cancellationToken = default)
        => db.WorkingTimePolicies.SingleOrDefaultAsync(p => p.EffectiveFrom == effectiveFrom, cancellationToken);

    public void AddPolicy(WorkingTimePolicy policy) => db.WorkingTimePolicies.Add(policy);
}
