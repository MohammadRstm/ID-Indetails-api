namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>
/// Archives completed projects nothing has happened on for 90 days, in domains whose <c>autoArchive</c> setting is on.
/// Run by the scheduler.
/// </summary>
public interface IProjectAutoArchive
{
    /// <summary>Archives what qualifies now, each project in its own transaction, and says how many.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
