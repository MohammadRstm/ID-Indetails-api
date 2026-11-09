using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

/// <summary>Company-wide counts for the overview, across both domains, each in one query. Money comes from the shared calculator.</summary>
public interface ICompanyFinanceRepository
{
    /// <summary>Every project of both domains, not tracked.</summary>
    Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active employees per domain.</summary>
    Task<IReadOnlyDictionary<string, int>> EmployeesAsync(CancellationToken cancellationToken = default);

    /// <summary>Clients per domain.</summary>
    Task<IReadOnlyDictionary<string, int>> ClientsAsync(CancellationToken cancellationToken = default);
}
