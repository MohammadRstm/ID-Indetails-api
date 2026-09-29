using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Teams.Abstraction;

public interface ITeamRepository
{
    /// <summary>Not tracked.</summary>
    Task<Team?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, for renaming or deleting.</summary>
    Task<Team?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Teams of the given domains by name, each with how many non-deactivated people are in it.</summary>
    Task<IReadOnlyList<(Team Team, int MemberCount)>> ListAsync(
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken = default);

    /// <summary>Whether another team of the domain already has this name (ignoring case).</summary>
    Task<bool> NameExistsAsync(string domainCode, string name, long? exceptTeamId, CancellationToken cancellationToken = default);

    /// <summary>Whether anyone, in any status, is in the team.</summary>
    Task<bool> HasPeopleAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>Whether the team's group chat exists, which it does from its first message.</summary>
    Task<bool> HasChatAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new team; saved by the unit of work.</summary>
    void Add(Team team);

    /// <summary>Stages deleting a team; saved by the unit of work.</summary>
    void Remove(Team team);
}
