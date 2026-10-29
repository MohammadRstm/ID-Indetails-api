using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

public sealed record PersonMaterialRow(long Id, long ProjectId, string Name, decimal Cost, DateTime AddedAt);

public sealed record PersonLabourWeekRow(long Id, long ProjectId, DateOnly WeekStart, decimal Total, DateTime SubmittedAt);

/// <param name="MaterialCount">Live expenses of the project.</param>
/// <param name="ReportedWeeks">Labour weeks reported on the project.</param>
/// <param name="LastMaterialAt">When the given person last added an expense there.</param>
/// <param name="LastWeekAt">When the given person last submitted a labour week there.</param>
public sealed record SiteActivityRow(long ProjectId, int MaterialCount, int ReportedWeeks, DateTime? LastMaterialAt, DateTime? LastWeekAt)
{
    public DateTime? LastRecordedAt => new[] { LastMaterialAt, LastWeekAt }.Max();
}

/// <summary>What was recorded on Execution sites, read for people and for project cards.</summary>
public interface ISiteRecordsRepository
{
    /// <summary>Live materials the person added, on projects of <paramref name="domains"/>.</summary>
    Task<IReadOnlyList<PersonMaterialRow>> MaterialsByAsync(long userId, IReadOnlyCollection<string> domains, CancellationToken cancellationToken = default);

    /// <summary>Labour weeks the person submitted, with their totals, on projects of <paramref name="domains"/>.</summary>
    Task<IReadOnlyList<PersonLabourWeekRow>> LabourWeeksByAsync(long userId, IReadOnlyCollection<string> domains, CancellationToken cancellationToken = default);

    /// <summary>Execution projects among <paramref name="projectIds"/> in <paramref name="domains"/>, not tracked.</summary>
    Task<IReadOnlyList<Project>> SitesAsync(IReadOnlyCollection<long> projectIds, IReadOnlyCollection<string> domains, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteActivityRow>> ActivityAsync(IReadOnlyCollection<long> projectIds, long userId, CancellationToken cancellationToken = default);
}
