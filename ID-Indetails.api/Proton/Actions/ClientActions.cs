using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Clients.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record ClientContactInput
{
    public required ContactKind Kind { get; init; }

    [Description("The email address or phone number, as the user gave it.")]
    public required string Value { get; init; }

    [Description("What the contact is (\"Main\", \"Site manager\"). Default Main.")]
    public string? Label { get; init; }

    [Description("A phone number on WhatsApp.")]
    public bool? Whatsapp { get; init; }
}

public sealed record CreateClientArgs(CreateClientRequest Request);

public sealed record CreateClientInput : ProtonActionInput
{
    [Description("The client's name, as the user said it.")]
    public required string Name { get; init; }

    public string? Organisation { get; init; }

    [Description("What kind of client (\"Client\", \"Developer\", \"Architect\"). Default Client.")]
    public string? Label { get; init; }

    public string? Note { get; init; }

    [Description("design or execution. Needed only when the user sees both: ask them if they didn't say.")]
    public string? Domain { get; init; }

    [Description("Only contacts the user gave; never made up.")]
    public IReadOnlyList<ClientContactInput>? Contacts { get; init; }
}

/// <summary>Adds a client, through POST /api/clients once confirmed.</summary>
public sealed class ProposeCreateClientTool(IClientService clients) : ProtonActionTool<CreateClientInput, CreateClientArgs>
{
    public override string Name => "propose_create_client";

    public override string Description =>
        "Propose adding a client with the contacts the user gave. Warns when the name is already used; a contact another " +
        "client has is refused. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override bool OpensByDefault => true;

    protected override string OpensLabel(CreateClientArgs args, ProtonRef? target) => "the new client";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Create));

    protected override async Task<(CreateClientArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateClientInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = ClientText.DomainOf(input.Domain, context);
        var request = new CreateClientRequest
        {
            Name = input.Name.Trim(),
            Organisation = ClientText.Clean(input.Organisation),
            Label = ClientText.Clean(input.Label) ?? "Client",
            Note = ClientText.Clean(input.Note),
            DomainCode = domain,
            Contacts = ClientText.ContactsOf(input.Contacts),
        };

        var check = await clients.CheckAsync(new CheckClientRequest
        {
            DomainCode = domain,
            Name = request.Name,
            Contacts = request.Contacts.Select(c => new CheckClientContactRequest { Kind = c.Kind, Value = c.Value }).ToList(),
        }, cancellationToken);

        var changes = new List<ProtonChange> { new("Name", null, request.Name) };
        if (request.Organisation is not null)
        {
            changes.Add(new ProtonChange("Organisation", null, request.Organisation));
        }

        changes.Add(new ProtonChange("Label", null, request.Label));
        changes.AddRange(request.Contacts.Select(c => new ProtonChange(ClientText.ContactLabel(c), null, c.Value)));
        if (request.Note is not null)
        {
            changes.Add(new ProtonChange("Note", null, ProtonText.Cut(request.Note, 200)!));
        }

        return (new CreateClientArgs(request), new ProtonPreview($"Add the client {request.Name}", changes, ClientText.Warning(check), Area: domain), null);
    }

    protected override object RequestOf(CreateClientArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CreateClientArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var client = await clients.CreateAsync(args.Request, cancellationToken);
        var created = ClientText.Ref(client);
        return new ProtonActionDone($"Added the client {client.DisplayName}.", "client", client.Id, [created], ProtonUiCommand.Open(created));
    }
}

/// <param name="Before">The client as it was when proposed: if it changed since, the update is refused, not applied over it.</param>
public sealed record UpdateClientArgs(long ClientId, UpdateClientRequest Request, UpdateClientRequest Before);

public sealed record UpdateClientInput : ProtonActionInput
{
    public required long ClientId { get; init; }

    public string? Name { get; init; }

    public string? Organisation { get; init; }

    public string? Label { get; init; }

    [Description("The new note; an empty string removes it.")]
    public string? Note { get; init; }

    [Description("Contacts to add. Existing contacts stay unless listed in remove_contact_ids.")]
    public IReadOnlyList<ClientContactInput>? AddContacts { get; init; }

    [Description("Ids of contacts to remove (from get_client).")]
    public IReadOnlyList<long>? RemoveContactIds { get; init; }
}

/// <summary>Edits a client's details and contacts, through PUT /api/clients/{id} once confirmed.</summary>
public sealed class ProposeUpdateClientTool(IClientService clients) : ProtonActionTool<UpdateClientInput, UpdateClientArgs>
{
    public override string Name => "propose_update_client";

    public override string Description =>
        "Propose changing a client's name, organisation, label or note, or adding and removing contacts (use get_client " +
        "first). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Update));

    protected override async Task<(UpdateClientArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateClientInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await clients.GetAsync(input.ClientId, cancellationToken);
        var before = ClientText.RequestOf(current);
        var removed = input.RemoveContactIds ?? [];
        var unknown = removed.Where(id => current.Contacts.All(c => c.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ProtonToolInputException($"{current.DisplayName} has no contact {string.Join(", ", unknown)}: use the ids from get_client.");
        }

        var kept = current.Contacts.Where(c => !removed.Contains(c.Id)).Select(ClientText.RequestOf).ToList();
        var added = ClientText.ContactsOf(input.AddContacts);
        var after = new UpdateClientRequest
        {
            Name = input.Name?.Trim() ?? before.Name,
            Organisation = input.Organisation is null ? before.Organisation : ClientText.Clean(input.Organisation),
            Label = ClientText.Clean(input.Label) ?? before.Label,
            Note = input.Note is null ? before.Note : ClientText.Clean(input.Note),
            Contacts = [.. kept, .. added],
        };

        var changes = new List<ProtonChange>();
        void Compare(string label, string? was, string? now)
        {
            if (was != now)
            {
                changes.Add(new ProtonChange(label, was ?? "none", now ?? "none"));
            }
        }

        Compare("Name", before.Name, after.Name);
        Compare("Organisation", before.Organisation, after.Organisation);
        Compare("Label", before.Label, after.Label);
        Compare("Note", ProtonText.Cut(before.Note, 200), ProtonText.Cut(after.Note, 200));
        changes.AddRange(current.Contacts.Where(c => removed.Contains(c.Id)).Select(c => new ProtonChange($"Remove {ClientText.ContactLabel(ClientText.RequestOf(c))}", c.Value, "removed")));
        changes.AddRange(added.Select(c => new ProtonChange($"Add {ClientText.ContactLabel(c)}", null, c.Value)));
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        string? note = null;
        if (after.Name != before.Name || added.Count > 0)
        {
            note = ClientText.Warning(await clients.CheckAsync(new CheckClientRequest
            {
                DomainCode = current.Domain,
                Name = after.Name,
                Contacts = added.Select(c => new CheckClientContactRequest { Kind = c.Kind, Value = c.Value }).ToList(),
                ExceptClientId = current.Id,
            }, cancellationToken));
        }

        return (new UpdateClientArgs(current.Id, after, before), new ProtonPreview($"Change the client {current.DisplayName}", changes, note), ClientText.Ref(current));
    }

    protected override object RequestOf(UpdateClientArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(UpdateClientArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await clients.GetAsync(args.ClientId, cancellationToken);
        if (!ClientText.Same(ClientText.RequestOf(current), args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The client changed since Proton proposed this. Ask again.");
        }

        var updated = await clients.UpdateAsync(args.ClientId, args.Request, cancellationToken);
        var target = ClientText.Ref(updated);
        return new ProtonActionDone($"Updated the client {updated.DisplayName}.", "client", updated.Id, [target], ProtonUiCommand.Open(target));
    }
}

public sealed record SetClientArchivedArgs(long ClientId, bool Archived);

public sealed record SetClientArchivedInput : ProtonActionInput
{
    public required long ClientId { get; init; }

    [Description("True to archive the client (hidden from lists, kept with its projects), false to restore it.")]
    public required bool Archived { get; init; }
}

/// <summary>Archives or restores a client, through POST /api/clients/{id}/archive or /restore once confirmed.</summary>
public sealed class ProposeSetClientArchivedTool(IClientService clients) : ProtonActionTool<SetClientArchivedInput, SetClientArchivedArgs>
{
    public override string Name => "propose_set_client_archived";

    public override string Description =>
        "Propose archiving a client (it leaves the lists; its projects and history stay) or restoring an archived one. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    // Archive and Restore share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Archive));

    protected override async Task<(SetClientArchivedArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SetClientArchivedInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var client = await clients.GetAsync(input.ClientId, cancellationToken);
        if (client.IsArchived == input.Archived)
        {
            throw new ProtonToolInputException(client.IsArchived ? $"{client.DisplayName} is already archived." : $"{client.DisplayName} isn't archived.");
        }

        var preview = new ProtonPreview(
            input.Archived ? $"Archive the client {client.DisplayName}" : $"Restore the client {client.DisplayName}",
            [new ProtonChange("State", client.IsArchived ? "Archived" : "Active", input.Archived ? "Archived" : "Active")],
            input.Archived && client.ProjectCount > 0 ? $"Its {client.ProjectCount} project(s) stay as they are." : null);
        return (new SetClientArchivedArgs(client.Id, input.Archived), preview, ClientText.Ref(client));
    }

    protected override object? RequestOf(SetClientArchivedArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(SetClientArchivedArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var client = await clients.SetArchivedAsync(args.ClientId, args.Archived, cancellationToken);
        return new ProtonActionDone(
            client.IsArchived ? $"Archived {client.DisplayName}." : $"Restored {client.DisplayName}.", "client", client.Id, [ClientText.Ref(client)]);
    }
}

/// <summary>Shared wording and mapping of the client actions.</summary>
internal static class ClientText
{
    public static ProtonRef Ref(ClientResponse c) => new(ProtonRefType.Client, c.Id, c.DisplayName, c.Domain);

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The domain a new record goes to: the one asked for, or the only one the user works in.</summary>
    public static string DomainOf(string? asked, ProtonToolContext context)
    {
        var visible = context.Access.VisibleDomains;
        if (asked is { Length: > 0 })
        {
            var domain = asked.Trim().ToLowerInvariant();
            return visible.Contains(domain) ? domain : throw new ProtonToolInputException($"The user can't add records to {asked}.");
        }

        return visible.Count == 1 ? visible.First() : throw new ProtonToolInputException("Design or Execution? Ask the user which business this is for.");
    }

    public static IReadOnlyList<ClientContactRequest> ContactsOf(IReadOnlyList<ClientContactInput>? contacts)
        => (contacts ?? []).Select(c => new ClientContactRequest
        {
            Kind = c.Kind,
            Value = c.Value.Trim(),
            Label = Clean(c.Label) ?? "Main",
            IsWhatsapp = c.Kind == ContactKind.Phone && c.Whatsapp == true,
        }).ToList();

    public static ClientContactRequest RequestOf(ClientContactResponse c)
        => new() { Kind = c.Kind, Value = c.Value, Label = c.Label, IsWhatsapp = c.IsWhatsapp, IsPrimary = c.IsPrimary };

    public static UpdateClientRequest RequestOf(ClientResponse c) => new()
    {
        Name = c.Name,
        Organisation = c.Organisation,
        Label = c.Label ?? "Client",
        Note = c.Note,
        Contacts = c.Contacts.Select(RequestOf).ToList(),
    };

    public static bool Same(UpdateClientRequest a, UpdateClientRequest b)
        => a.Name == b.Name && a.Organisation == b.Organisation && a.Label == b.Label && a.Note == b.Note && a.Contacts.SequenceEqual(b.Contacts);

    public static string ContactLabel(ClientContactRequest c)
        => $"{(c.Kind == ContactKind.Email ? "Email" : c.IsWhatsapp ? "WhatsApp" : "Phone")}{(c.Label is { Length: > 0 } label && label != "Main" ? $" ({label})" : "")}";

    /// <summary>
    /// The card's note about a name already used (allowed: two clients may share one). A contact another client has is
    /// refused by the service, so it is refused here before any card is made.
    /// </summary>
    public static string? Warning(ClientCheckResponse check)
    {
        if (check.ContactTakenBy is { } taken)
        {
            throw new ProtonToolInputException($"That {(taken.Kind == ContactKind.Email ? "email" : "phone number")} already belongs to {taken.Name}.");
        }

        return check.NameTaken ? "A client with this name already exists." : null;
    }
}
