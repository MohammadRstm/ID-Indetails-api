using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Clients.Implementation;

public sealed class ClientRepository(IdIndetailsDbContext db) : IClientRepository
{
    public async Task<IReadOnlyList<ClientAdded>> AddedAsync(IReadOnlyList<string> domains, string? domain, CancellationToken cancellationToken = default)
    {
        var codes = domains.ToList();
        return await db.Clients.AsNoTracking()
            .Where(c => codes.Contains(c.DomainCode) && (domain == null || c.DomainCode == domain))
            .Select(c => new ClientAdded(c.Id, c.Name, c.CreatedAt, c.ArchivedAt != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ClientRow> Items, int TotalCount)> SearchAsync(
        ClientSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Clients.AsNoTracking().Where(c => domains.Contains(c.DomainCode));

        if (!search.IncludeArchived)
        {
            query = search.Archived == true
                ? query.Where(c => c.ArchivedAt != null)
                : query.Where(c => c.ArchivedAt == null);
        }

        if (search.Domain is { } domain)
        {
            query = query.Where(c => c.DomainCode == domain);
        }

        // Every word must appear in the name, organisation, label, note or a contact (the collation ignores case).
        foreach (var word in (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(c => c.Name.Contains(word)
                || (c.Organisation != null && c.Organisation.Contains(word))
                || (c.Label != null && c.Label.Contains(word))
                || (c.Note != null && c.Note.Contains(word))
                || c.Contacts.Any(x => x.Value.Contains(word) || x.NormalizedValue.Contains(word)));
        }

        if (search.Filter == ClientFilter.Active)
        {
            query = query.Where(c => db.Projects.Any(p => p.ClientId == c.Id && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Hold)));
        }
        else if (search.Filter == ClientFilter.Empty)
        {
            query = query.Where(c => !db.Projects.Any(p => p.ClientId == c.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var live = db.WorkEntries.Where(e => e.DeletedAt == null);
        var ordered = search.Sort switch
        {
            ClientSort.Projects => query.OrderByDescending(c => db.Projects.Count(p => p.ClientId == c.Id)),
            ClientSort.Hours => query.OrderByDescending(c => live.Where(e => e.Project.ClientId == c.Id).Sum(e => (int?)e.DurationMinutes) ?? 0),
            ClientSort.Activity => query.OrderByDescending(c => live.Where(e => e.Project.ClientId == c.Id).Max(e => (DateTime?)e.StartedAt) ?? c.CreatedAt),
            _ => query.OrderBy(c => c.Organisation ?? c.Name),
        };

        var rows = await ordered
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

    public async Task<IReadOnlyList<ClientWork>> WorkAsync(IReadOnlyCollection<long> clientIds, CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => e.DeletedAt == null && e.Project.ClientId != null && clientIds.Contains(e.Project.ClientId.Value))
            .GroupBy(e => e.Project.ClientId!.Value)
            .Select(g => new ClientWork(g.Key, g.Sum(e => e.DurationMinutes), g.Max(e => e.StartedAt)))
            .ToListAsync(cancellationToken);

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
