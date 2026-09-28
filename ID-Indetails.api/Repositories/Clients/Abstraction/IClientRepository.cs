using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Clients.Abstraction;

public sealed record ClientSearch(
    IReadOnlyList<string> Domains,
    string? Text,
    bool? Archived,
    string? Domain,
    int Page,
    int PageSize);

/// <summary>A client with its contacts and how many projects use it.</summary>
public sealed record ClientRow(Client Client, int ProjectCount);

public interface IClientRepository
{
    Task<(IReadOnlyList<ClientRow> Items, int TotalCount)> SearchAsync(ClientSearch search, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with contacts.</summary>
    Task<ClientRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with contacts.</summary>
    Task<Client?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<int> CountProjectsAsync(long clientId, CancellationToken cancellationToken = default);

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
