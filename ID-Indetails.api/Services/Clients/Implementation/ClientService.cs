using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Clients.Implementation;

/// <summary>Clients of the caller's visible domains. Clients of any other domain read as not found.</summary>
public sealed class ClientService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IClientRepository clients,
    IMoneySummaryRepository moneySummaries,
    IMoneyRollupService moneyRollups,
    IWorkEntryRepository entries,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IClientService
{
    private const string NotFoundMessage = "Client not found.";
    public const int WorkMonths = 12;
    public const int WorkPeople = 8;

    public async Task<ClientOverviewResponse> OverviewAsync(ClientOverviewQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        async Task<int> CountAsync(bool? archived, ClientFilter? filter)
            => (await clients.SearchAsync(new ClientSearch(actor.VisibleDomains, null, archived, query.Domain, 1, 1, filter), cancellationToken)).TotalCount;

        var today = clock.CompanyToday;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var added = await clients.AddedAsync(actor.VisibleDomains, query.Domain, cancellationToken);
        ClientAddedResponse Brief(ClientAdded c) => new(c.Id, c.Name, c.CreatedAt);
        var byId = added.ToDictionary(c => c.Id);
        var growth = ClientGrowth.Of(added.Select(c => new ClientGrowth.Added(c.Id, c.Name, clock.ToCompanyDate(c.CreatedAt))).ToList(), today);

        return new ClientOverviewResponse(
            await CountAsync(null, null),
            await CountAsync(true, null),
            await CountAsync(null, ClientFilter.Active),
            await CountAsync(null, ClientFilter.Empty),
            added.Count(c => !c.IsArchived && clock.ToCompanyDate(c.CreatedAt) >= thisMonth),
            added.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Select(Brief).FirstOrDefault(),
            growth.Select(m => new ClientGrowthMonthResponse(m.Start, m.Total, m.AddedInMonth.Select(a => Brief(byId[a.Id])).ToList())).ToList());
    }

    public async Task<PagedResponse<ClientSummaryResponse>> ListAsync(ClientListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var byMoney = query.Filter == ClientFilter.Owing
            || query.Sort is ClientSort.Revenue or ClientSort.Profit or ClientSort.Margin or ClientSort.Outstanding;
        if (byMoney && !actor.Has(PermissionCodes.FinancialView))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Sorting or filtering clients by money needs financial:view.");
        }

        var search = new ClientSearch(
            actor.VisibleDomains, query.Search?.Trim(), query.Archived, query.Domain, query.Page, query.PageSize, query.Filter, query.Sort);
        IReadOnlyList<ClientRow> rows;
        int total;
        IReadOnlyDictionary<long, ClientMoneyResponse>? money = null;
        if (byMoney)
        {
            // The money of every matching client is needed to filter and sort, so they are paged here.
            var (all, _) = await clients.SearchAsync(search with { Page = 1, PageSize = int.MaxValue }, cancellationToken);
            var allMoney = await MoneyOfAsync(all, cancellationToken);
            var matching = all.Where(r => query.Filter != ClientFilter.Owing || allMoney[r.Client.Id].Billing.Outstanding > 0).ToList();
            rows = SortedByMoney(matching, allMoney, query.Sort).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
            total = matching.Count;
            money = allMoney;
        }
        else
        {
            (rows, total) = await clients.SearchAsync(search, cancellationToken);
            money = actor.Has(PermissionCodes.FinancialView) ? await MoneyOfAsync(rows, cancellationToken) : null;
        }

        var work = actor.Has(PermissionCodes.WorkEntryViewAll)
            ? (await clients.WorkAsync(rows.Select(r => r.Client.Id).ToList(), cancellationToken)).ToDictionary(w => w.ClientId)
            : null;
        return new PagedResponse<ClientSummaryResponse>(
            rows.Select(r => ToSummary(r) with
                {
                    Money = money?[r.Client.Id],
                    Work = work is null ? null
                        : work.TryGetValue(r.Client.Id, out var w) ? new ClientListWorkResponse(w.Minutes, w.LastStartedAt)
                        : new ClientListWorkResponse(0, null),
                })
                .ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ClientResponse> GetAsync(long clientId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var client = await DescribeAsync(actor, clientId, cancellationToken);
        if (actor.Has(PermissionCodes.WorkEntryViewAll))
        {
            client = client with { Work = await WorkOfAsync(clientId, cancellationToken) };
        }

        if (!actor.Has(PermissionCodes.FinancialView))
        {
            return client;
        }

        var projects = await moneySummaries.ClientProjectsAsync([clientId], cancellationToken);
        var rollup = await moneyRollups.LoadAsync(projects, cancellationToken);
        return client with
        {
            Money = rollup.ClientMoney(projects),
            ProjectMoney = projects.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id).Select(rollup.ClientProject).ToList(),
        };
    }

    public async Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var domain = request.DomainCode ?? (actor.VisibleDomains.Count == 1 ? actor.VisibleDomains[0] : null)
            ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "domainCode", "Choose the domain the client belongs to.");

        if (!actor.CanSee(domain))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You can only add clients in a domain you work in.");
        }

        var contacts = await PrepareContactsAsync(domain, request.Contacts, null, cancellationToken);
        var client = new Client
        {
            DomainCode = domain,
            Name = request.Name.Trim(),
            Organisation = Clean(request.Organisation),
            Label = Clean(request.Label),
            Note = Clean(request.Note),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
            Contacts = contacts,
        };
        clients.Add(client);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(ClientEvent(client, ActivityEventKinds.ClientAdded, actor.UserId));
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.ClientAdded, actor.UserId, client.DomainCode, null, "client", client.Id,
                new { name = client.Name, organisation = client.Organisation }),
            await notifications.HoldersAsync(client.DomainCode, PermissionCodes.ClientManage, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(actor, client.Id, cancellationToken);
    }

    public async Task<ClientResponse> UpdateAsync(long clientId, UpdateClientRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var client = await VisibleClientForUpdateAsync(actor, clientId, cancellationToken);

        var contacts = await PrepareContactsAsync(client.DomainCode, request.Contacts, client.Id, cancellationToken);
        client.Name = request.Name.Trim();
        client.Organisation = Clean(request.Organisation);
        client.Label = Clean(request.Label);
        client.Note = Clean(request.Note);
        client.UpdatedAt = clock.UtcNow;
        client.Contacts.Clear();
        foreach (var contact in contacts)
        {
            client.Contacts.Add(contact);
        }

        activity.Add(ClientEvent(client, ActivityEventKinds.ClientUpdated, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(actor, client.Id, cancellationToken);
    }

    public async Task<ClientResponse> SetArchivedAsync(long clientId, bool archived, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var client = await VisibleClientForUpdateAsync(actor, clientId, cancellationToken);
        Throw(ClientRules.CheckArchive(client.ArchivedAt is not null, archived));

        client.ArchivedAt = archived ? clock.UtcNow : null;
        client.UpdatedAt = clock.UtcNow;
        activity.Add(ClientEvent(client, archived ? ActivityEventKinds.ClientArchived : ActivityEventKinds.ClientRestored, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(actor, client.Id, cancellationToken);
    }

    public async Task DeleteAsync(long clientId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var client = await VisibleClientForUpdateAsync(actor, clientId, cancellationToken);
        Throw(ClientRules.CheckDelete(await clients.CountProjectsAsync(client.Id, cancellationToken)));

        // The event keeps the name, since the client row (and its contacts) are gone afterwards.
        activity.Add(ClientEvent(client, ActivityEventKinds.ClientDeleted, actor.UserId, new { name = client.Name }));
        clients.Remove(client);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<Client> VisibleClientForUpdateAsync(UserAccess actor, long clientId, CancellationToken cancellationToken)
    {
        var client = await clients.GetForUpdateAsync(clientId, cancellationToken);
        return client is not null && actor.CanSee(client.DomainCode) ? client : throw new NotFoundException(NotFoundMessage);
    }

    private async Task<ClientResponse> DescribeAsync(UserAccess actor, long clientId, CancellationToken cancellationToken)
    {
        var row = await clients.GetAsync(clientId, cancellationToken);
        if (row is null || !actor.CanSee(row.Client.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        var c = row.Client;
        return new ClientResponse
        {
            Id = c.Id,
            Domain = c.DomainCode,
            Name = c.Name,
            Organisation = c.Organisation,
            DisplayName = ClientRules.DisplayName(c.Name, c.Organisation),
            Label = c.Label,
            Note = c.Note,
            IsArchived = c.ArchivedAt is not null,
            CreatedAt = c.CreatedAt,
            Contacts = c.Contacts.Select(ToContact).ToList(),
            ProjectCount = row.ProjectCount,
            NameSharedWithAnotherClient = await clients.NameExistsAsync(c.DomainCode, c.Name, c.Id, cancellationToken),
        };
    }

    /// <summary>The request's contacts, ready to store, after checking no other client of the domain holds one of them.</summary>
    private async Task<List<ClientContact>> PrepareContactsAsync(
        string domain,
        IReadOnlyList<ClientContactRequest> request,
        long? clientId,
        CancellationToken cancellationToken)
    {
        var (prepared, denial) = ClientRules.PrepareContacts(
            request.Select(c => new ClientRules.ContactInput(c.Kind, c.Value, c.Label, c.IsWhatsapp, c.IsPrimary)).ToList());
        Throw(denial);

        var owner = await clients.FindContactOwnerAsync(
            domain, prepared.Select(c => (c.Kind, c.NormalizedValue)).ToList(), clientId, cancellationToken);
        if (owner is { } taken)
        {
            throw new ConflictException(
                ErrorCodes.ContactTaken,
                ClientRules.ContactTakenMessage(taken.Kind, ClientRules.DisplayName(taken.Client.Name, taken.Client.Organisation)));
        }

        return prepared.Select(c => new ClientContact
        {
            Kind = c.Kind,
            Value = c.Value,
            NormalizedValue = c.NormalizedValue,
            Label = c.Label,
            IsWhatsapp = c.IsWhatsapp,
            IsPrimary = c.IsPrimary,
        }).ToList();
    }

    /// <summary>
    /// The client is only the event's subject: <c>activity_events.client_id</c> is a restricting
    /// foreign key, and setting it would stop a client with history from ever being deleted.
    /// </summary>
    private ActivityEvent ClientEvent(Client client, string kind, long actorId, object? payload = null) => new()
    {
        DomainCode = client.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        SubjectType = "client",
        SubjectId = client.Id,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The client's logged work: the last 12 months and who logged it, most time first.</summary>
    private async Task<ClientWorkResponse> WorkOfAsync(long clientId, CancellationToken cancellationToken)
    {
        var rows = await entries.RowsAsync(new WorkEntrySearch(ClientId: clientId), cancellationToken);
        var thisMonth = WorkStats.MonthStartOf(clock.CompanyToday);
        var byMonth = rows
            .GroupBy(r => WorkStats.MonthStartOf(clock.ToCompanyDate(r.StartedAt)))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Minutes));
        var byPerson = rows
            .GroupBy(r => r.UserId)
            .Select(g => (UserId: g.Key, Minutes: g.Sum(r => r.Minutes)))
            .OrderByDescending(p => p.Minutes)
            .ThenBy(p => p.UserId)
            .ToList();
        var top = byPerson.Take(WorkPeople).ToList();
        var names = (await entries.PeopleAsync(top.Select(p => p.UserId).ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Name);

        return new ClientWorkResponse(
            rows.Sum(r => r.Minutes),
            rows.Count,
            byPerson.Count,
            Enumerable.Range(0, WorkMonths)
                .Select(i => thisMonth.AddMonths(i - WorkMonths + 1))
                .Select(month => new MonthMinutesResponse(month, byMonth.GetValueOrDefault(month)))
                .ToList(),
            top.Select(p => new ClientPersonWorkResponse(p.UserId, names.GetValueOrDefault(p.UserId, string.Empty), p.Minutes)).ToList());
    }

    /// <summary>The money of each client across their projects, in one batched load.</summary>
    private async Task<IReadOnlyDictionary<long, ClientMoneyResponse>> MoneyOfAsync(IReadOnlyList<ClientRow> rows, CancellationToken cancellationToken)
    {
        var projects = (await moneySummaries.ClientProjectsAsync(rows.Select(r => r.Client.Id).ToList(), cancellationToken))
            .ToLookup(p => p.ClientId!.Value);
        var rollup = await moneyRollups.LoadAsync(projects.SelectMany(g => g).ToList(), cancellationToken);
        return rows.ToDictionary(r => r.Client.Id, r => rollup.ClientMoney(projects[r.Client.Id].ToList()));
    }

    private static IEnumerable<ClientRow> SortedByMoney(
        IEnumerable<ClientRow> rows,
        IReadOnlyDictionary<long, ClientMoneyResponse> money,
        ClientSort sort)
    {
        var ordered = sort switch
        {
            ClientSort.Revenue => rows.OrderByDescending(r => money[r.Client.Id].Revenue),
            ClientSort.Profit => rows.OrderByDescending(r => money[r.Client.Id].Profit),
            ClientSort.Margin => rows.OrderByDescending(r => money[r.Client.Id].Margin ?? decimal.MinValue),
            ClientSort.Outstanding => rows.OrderByDescending(r => money[r.Client.Id].Billing.Outstanding),
            // Filtered by money but sorted by something else: the repository's order stands.
            _ => null,
        };

        return ordered?.ThenBy(r => ClientRules.DisplayName(r.Client.Name, r.Client.Organisation), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Client.Id) ?? rows;
    }

    private static ClientContactResponse ToContact(ClientContact x)
        => new(x.Id, x.Kind, x.Value, x.Label, x.IsWhatsapp, x.IsPrimary);

    private static ClientSummaryResponse ToSummary(ClientRow row) => new()
    {
        Id = row.Client.Id,
        Domain = row.Client.DomainCode,
        Name = row.Client.Name,
        Organisation = row.Client.Organisation,
        DisplayName = ClientRules.DisplayName(row.Client.Name, row.Client.Organisation),
        Label = row.Client.Label,
        IsArchived = row.Client.ArchivedAt is not null,
        PrimaryContact = row.Client.Contacts.Where(x => x.IsPrimary).Select(ToContact).FirstOrDefault(),
        ProjectCount = row.ProjectCount,
    };
}
