using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Clients.Abstraction;

/// <param name="Filter">Active and empty are applied here; owing needs the money, so the service applies it.</param>
/// <param name="Sort">The money sorts need the money, so the service applies them; here they order by name.</param>
/// <param name="Archived">True for archived clients only; otherwise the ones not archived.</param>
/// <param name="IncludeArchived">Both, whatever <paramref name="Archived"/> says.</param>
public sealed record ClientSearch(
    IReadOnlyList<string> Domains,
    string? Text,
    bool? Archived,
    string? Domain,
    int Page,
    int PageSize,
    ClientFilter? Filter = null,
    ClientSort Sort = ClientSort.Name,
    bool IncludeArchived = false);

/// <summary>A client with its contacts and how many projects use it.</summary>
public sealed record ClientRow(Client Client, int ProjectCount);

public sealed record ClientAdded(long Id, string Name, DateTime CreatedAt, bool IsArchived);

/// <summary>The live work logged on a client's projects.</summary>
public sealed record ClientWork(long ClientId, int Minutes, DateTime LastStartedAt);

public interface IClientRepository
{
    Task<(IReadOnlyList<ClientRow> Items, int TotalCount)> SearchAsync(ClientSearch search, CancellationToken cancellationToken = default);

    /// <summary>When every client of these domains (or of <paramref name="domain"/>) was added, archived ones included. Not tracked.</summary>
    Task<IReadOnlyList<ClientAdded>> AddedAsync(IReadOnlyList<string> domains, string? domain, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with contacts.</summary>
    Task<ClientRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with contacts.</summary>
    Task<Client?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<int> CountProjectsAsync(long clientId, CancellationToken cancellationToken = default);

    /// <summary>The work logged on each client's projects; clients without any are left out.</summary>
    Task<IReadOnlyList<ClientWork>> WorkAsync(IReadOnlyCollection<long> clientIds, CancellationToken cancellationToken = default);

    /// <summary>The first client of the domain (other than <paramref name="exceptClientId"/>) holding one of these contacts.</summary>
    Task<(Client Client, ContactKind Kind)?> FindContactOwnerAsync(
        string domainCode,
        IReadOnlyList<(ContactKind Kind, string NormalizedValue)> contacts,
        long? exceptClientId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether another client of the domain has this name (ignoring case).</summary>
    Task<bool> NameExistsAsync(string domainCode, string name, long exceptClientId, CancellationToken cancellationToken = default);

    void Add(Client client);

    void Remove(Client client);
}
