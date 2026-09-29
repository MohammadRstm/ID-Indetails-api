using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;

namespace ID_Indetails.api.Services.Clients.Implementation;

/// <summary>Clients of the caller's visible domains. Clients of any other domain read as not found.</summary>
public sealed class ClientService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IClientRepository clients,
    IMoneySummaryRepository moneySummaries,
    IMoneyRollupService moneyRollups,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IClientService
{
    private const string NotFoundMessage = "Client not found.";

    public async Task<PagedResponse<ClientSummaryResponse>> ListAsync(ClientListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var (rows, total) = await clients.SearchAsync(
            new ClientSearch(actor.VisibleDomains, query.Search?.Trim(), query.Archived, query.Domain, query.Page, query.PageSize),
            cancellationToken);

        if (!actor.Has(PermissionCodes.FinancialView))
        {
            return new PagedResponse<ClientSummaryResponse>(rows.Select(ToSummary).ToList(), query.Page, query.PageSize, total);
        }

        // One batched load for the whole page, never a query per client.
        var projects = (await moneySummaries.ClientProjectsAsync(rows.Select(r => r.Client.Id).ToList(), cancellationToken))
            .ToLookup(p => p.ClientId!.Value);
        var rollup = await moneyRollups.LoadAsync(projects.SelectMany(g => g).ToList(), cancellationToken);
        return new PagedResponse<ClientSummaryResponse>(
            rows.Select(r => ToSummary(r) with { Money = rollup.ClientMoney(projects[r.Client.Id].ToList()) }).ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ClientResponse> GetAsync(long clientId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var client = await DescribeAsync(actor, clientId, cancellationToken);
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
