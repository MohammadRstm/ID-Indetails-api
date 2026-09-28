using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

public interface ICompanyRepository
{
    /// <summary>The single settings row, or null until it is first saved. Tracked when <paramref name="forUpdate"/>.</summary>
    Task<CompanySettings?> GetSettingsAsync(bool forUpdate = false, CancellationToken cancellationToken = default);

    void AddSettings(CompanySettings settings);

    /// <summary>Every working-time policy, not tracked.</summary>
    Task<IReadOnlyList<WorkingTimePolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<WorkingTimePolicy?> GetPolicyForUpdateAsync(DateOnly effectiveFrom, CancellationToken cancellationToken = default);

    void AddPolicy(WorkingTimePolicy policy);
}
