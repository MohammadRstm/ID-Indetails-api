using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Reports.Abstraction;

public interface IReportRepository
{
    /// <summary>Every Design project, with its client. Not tracked.</summary>
    Task<IReadOnlyList<Project>> DesignProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>When each of the projects was last put on hold, from its <c>project_hold</c> events.</summary>
    Task<IReadOnlyDictionary<long, DateTime>> HeldSinceAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);
}
