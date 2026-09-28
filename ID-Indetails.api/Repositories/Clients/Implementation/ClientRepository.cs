using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Clients.Implementation;

public sealed class ClientRepository(IdIndetailsDbContext db) : IClientRepository
{
    public async Task<(IReadOnlyList<ClientRow> Items, int TotalCount)> SearchAsync(
        ClientSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Clients.AsNoTracking().Where(c => domains.Contains(c.DomainCode));

        query = search.Archived == true
            ? query.Where(c => c.ArchivedAt != null)
            : query.Where(c => c.ArchivedAt == null);

        if (search.Domain is { } domain)
        {
            query = query.Where(c => c.DomainCode == domain);
        }

        // Every word must appear in the name or organisation (the collation ignores case).
        foreach (var word in (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(c => c.Name.Contains(word) || (c.Organisation != null && c.Organisation.Contains(word)));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(c => c.Organisation ?? c.Name)
            .ThenBy(c => c.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(c => new { Client = c, Contacts = c.Contacts.ToList(), Projects = db.Projects.Count(p => p.ClientId == c.Id) })
            .ToListAsync(cancellationToken);

        return (rows.Select(r => Row(r.Client, r.Contacts, r.Projects)).ToList(), total);
    }

    public async Task<ClientRow?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var row = await db.Clients
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { Client = c, Contacts = c.Contacts.ToList(), Projects = db.Projects.Count(p => p.ClientId == c.Id) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : Row(row.Client, row.Contacts, row.Projects);
    }

    public Task<Client?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.Clients.Include(c => c.Contacts).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<int> CountProjectsAsync(long clientId, CancellationToken cancellationToken = default)
        => db.Projects.CountAsync(p => p.ClientId == clientId, cancellationToken);

    public async Task<(Client Client, ContactKind Kind)?> FindContactOwnerAsync(
        string domainCode,
        IReadOnlyList<(ContactKind Kind, string NormalizedValue)> contacts,
        long? exceptClientId,
        CancellationToken cancellationToken = default)
    {
        foreach (var (kind, value) in contacts)
        {
            var owner = await db.ClientContacts
                .AsNoTracking()
                .Where(x => x.Kind == kind
                    && x.NormalizedValue == value
                    && x.Client.DomainCode == domainCode
                    && (exceptClientId == null || x.ClientId != exceptClientId))
                .Select(x => x.Client)
                .FirstOrDefaultAsync(cancellationToken);

            if (owner is not null)
            {
                return (owner, kind);
            }
        }

        return null;
    }

    public Task<bool> NameExistsAsync(string domainCode, string name, long exceptClientId, CancellationToken cancellationToken = default)
        => db.Clients.AnyAsync(c => c.DomainCode == domainCode && c.Name == name && c.Id != exceptClientId, cancellationToken);

    private static ClientRow Row(Client client, List<ClientContact> contacts, int projects)
    {
        client.Contacts = contacts.OrderBy(x => x.Id).ToList();
        return new ClientRow(client, projects);
    }

    public void Add(Client client) => db.Clients.Add(client);

    public void Remove(Client client) => db.Clients.Remove(client);
}
