using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.WorkEntries.Abstraction;

/// <summary>A work entry with what the API shows about it.</summary>
public sealed record WorkEntryView(
    long Id,
    long UserId,
    string UserName,
    long ProjectId,
    string ProjectTitle,
    ProjectStatus ProjectStatus,
    string ProjectDomain,
    long? ClientId,
    string? ClientName,
    string Description,
    DateTime StartedAt,
    DateTime EndedAt,
    int Minutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int CommentCount);

public interface IWorkEntryRepository
{
    /// <summary>
    /// Locks the person's user row until the transaction ends, so two changes to their entries are
    /// checked for overlaps one after the other.
    /// </summary>
    Task LockPersonAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its project. Live entries only.</summary>
    Task<WorkEntry?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its project. Deleted entries only.</summary>
    Task<WorkEntry?> GetDeletedForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with their projects. Live entries of <paramref name="userId"/> only.</summary>
    Task<IReadOnlyList<WorkEntry>> ListOwnForUpdateAsync(long userId, IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>The person's live entries overlapping [from, to). Not tracked.</summary>
    Task<IReadOnlyList<WorkEntryRules.Span>> SpansAsync(long userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>A live entry. Not tracked.</summary>
    Task<WorkEntryView?> GetViewAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active people who might hold <paramref name="permissionCode"/> in the domain: its members, super admins,
    /// and anyone granted it personally. The caller still checks each one's effective permissions.
    /// </summary>
    Task<IReadOnlyList<long>> PeopleWhoMayHoldAsync(string domainCode, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>Locks the domain row until the transaction ends, to serialise updates of shared digest notifications.</summary>
    Task LockDomainAsync(string domainCode, CancellationToken cancellationToken = default);

    void Add(WorkEntry entry);
}
