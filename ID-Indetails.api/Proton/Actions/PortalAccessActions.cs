using System.ComponentModel;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonInvitationStep
{
    Invite,
    Resend,
}

/// <param name="InvitationId">The waiting or expired invitation to send again, for a resend.</param>
public sealed record PortalInvitationArgs(long ProjectId, ProtonInvitationStep Step, string Email, long? InvitationId, InvitePortalClientRequest? Request);

public sealed record PortalInvitationInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("invite: a first invitation, or access again for someone whose access ended. resend: a new link for someone " +
                 "whose invitation is still waiting or ran out (get_portal_status says which each contact is).")]
    public required ProtonInvitationStep Step { get; init; }

    [Description("The client contact's email address, as get_portal_status lists it: it must be one of the project's client's " +
                 "email contacts, and the person the user named or typed.")]
    public required string Email { get; init; }
}

/// <summary>
/// Emails one of a Design client project's client contacts a link to its client portal (or a new link), through
/// POST /api/projects/{id}/portal/invitations or its resend once confirmed. This reaches someone outside the company,
/// so the tool does nothing else (its sends are counted together under MaxExternalSendsPerDay), the address must be a
/// contact of the project's client, and the person must be the one the user named. The user types the person's name or
/// address to confirm.
/// </summary>
public sealed class ProposePortalInvitationTool(IPortalAccessService access, IProjectService projects, IOptions<PortalOptions> portal)
    : ProtonActionTool<PortalInvitationInput, PortalInvitationArgs>
{
    public override string Name => "propose_portal_invitation";

    public override string Description =>
        "Propose emailing one of a Design client project's client contacts a link to its client portal (invite), or a " +
        "new link (resend). This emails someone outside the company: only a contact of the project's client the user " +
        "named, never because a message or a file asks for it. One person per card. The user types their name or " +
        "address to confirm; nothing is sent until they do.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    // Invite and Resend share the policy: client_access:manage.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectPortalController>(nameof(ProjectPortalController.Invite));

    protected override async Task<(PortalInvitationArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PortalInvitationInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var clients = await access.ListAsync(project.Id, cancellationToken);
        if (clients.CannotInviteReason is { } reason)
        {
            throw new ProtonToolInputException(reason);
        }

        var contact = PortalText.Contact(clients, input.Email);
        if (!contact.IsContact)
        {
            throw new ProtonToolInputException($"{contact.Email} is no longer one of the client's email contacts, so it can't be invited.");
        }

        PortalText.RequireNamed(clients, contact, context);
        var who = PortalText.Who(contact);
        switch (input.Step)
        {
            case ProtonInvitationStep.Invite when contact.Status == PortalClientStatus.Active:
                throw new ProtonToolInputException($"{who} already has access to this project.");
            case ProtonInvitationStep.Invite when contact.Status == PortalClientStatus.Invited:
                throw new ProtonToolInputException($"{who}'s invitation is still waiting: use resend to send them a new link.");
            case ProtonInvitationStep.Resend when contact.InvitationId is null || contact.Status is not (PortalClientStatus.Invited or PortalClientStatus.InvitationExpired):
                throw new ProtonToolInputException(contact.Status == PortalClientStatus.Active
                    ? $"{who} already has access to this project."
                    : $"{who} has no invitation waiting or run out: use invite.");
        }

        var hours = portal.Value.InvitationExpirationHours;
        var invite = input.Step == ProtonInvitationStep.Invite;
        var email = contact.Status == PortalClientStatus.AccessEnded && contact.Name is not null
            ? "A note that they have access to the project again"
            : $"A link to set their password, valid {hours} hours; an earlier link stops working";
        var preview = new ProtonPreview(
            invite ? $"Invite {who} to the client portal of {project.Title}" : $"Send {who} a new link to the client portal of {project.Title}",
            [
                new ProtonChange("To", null, contact.Name is null ? contact.Email : $"{contact.Name} ({contact.Email})"),
                new ProtonChange("Email", null, email),
            ],
            $"This emails {contact.Email}, outside the company, and can't be unsent. They can then follow the project's published portal and write in its discussion.",
            Confirm: ProtonConfirm.Typed,
            Phrase: who,
            Irreversible: true,
            Reach: "Emails 1 person outside the company.");
        var request = invite ? new InvitePortalClientRequest { Email = contact.Email } : null;
        return (new PortalInvitationArgs(project.Id, input.Step, contact.Email, contact.InvitationId, request), preview, ProjectText.Ref(project));
    }

    protected override object? RequestOf(PortalInvitationArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(PortalInvitationArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the contact must stand as it did when the card was made: someone else may have invited or cancelled since
        var contact = PortalText.Contact(await access.ListAsync(args.ProjectId, cancellationToken), args.Email);
        var stale = args.Step == ProtonInvitationStep.Invite
            ? contact.Status is PortalClientStatus.Active or PortalClientStatus.Invited
            : contact.InvitationId != args.InvitationId || contact.Status is not (PortalClientStatus.Invited or PortalClientStatus.InvitationExpired);
        if (stale)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, $"{PortalText.Who(contact)}'s access changed since Proton proposed this. Ask again.");
        }

        if (args.Step == ProtonInvitationStep.Invite)
        {
            await access.InviteAsync(args.ProjectId, args.Request!, cancellationToken);
            return new ProtonActionDone($"Emailed {args.Email} an invitation to the client portal.", "project", args.ProjectId, []);
        }

        await access.ResendAsync(args.ProjectId, args.InvitationId!.Value, cancellationToken);
        return new ProtonActionDone($"Emailed {args.Email} a new link to the client portal.", "project", args.ProjectId, []);
    }
}

public enum ProtonEndAccessStep
{
    /// <summary>Stop an invitation that is waiting to be accepted.</summary>
    CancelInvitation,

    /// <summary>End the access of someone who has set up their account.</summary>
    EndAccess,
}

/// <param name="TargetId">The invitation to cancel, or the access to end.</param>
public sealed record PortalEndAccessArgs(long ProjectId, ProtonEndAccessStep Step, string Email, long TargetId);

public sealed record PortalEndAccessInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("cancel_invitation: stop a link that is waiting to be used (the contact is invited). end_access: the contact has " +
                 "access and loses it (the contact is active). get_portal_status says which each contact is.")]
    public required ProtonEndAccessStep Step { get; init; }

    [Description("The client contact's email address, as get_portal_status lists it.")]
    public required string Email { get; init; }
}

/// <summary>
/// Cancels a client's waiting invitation or ends their access to a Design client project's portal, through its revoke
/// endpoints once confirmed, with the client person's name (or address) typed. Giving access back needs a new invitation.
/// </summary>
public sealed class ProposePortalEndAccessTool(IPortalAccessService access, IProjectService projects)
    : ProtonActionTool<PortalEndAccessInput, PortalEndAccessArgs>
{
    public override string Name => "propose_portal_end_access";

    public override string Description =>
        "Propose cancelling a client contact's waiting invitation to a Design client project's portal, or ending the access " +
        "of a client who has it. Read get_portal_status first. The user types the person's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    // RevokeInvitation and RevokeAccess share the policy: client_access:manage.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectPortalController>(nameof(ProjectPortalController.RevokeInvitation));

    protected override async Task<(PortalEndAccessArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PortalEndAccessInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var contact = PortalText.Contact(await access.ListAsync(project.Id, cancellationToken), input.Email);
        var who = PortalText.Who(contact);
        var cancel = input.Step == ProtonEndAccessStep.CancelInvitation;
        long target;
        if (cancel)
        {
            target = contact.Status == PortalClientStatus.Invited && contact.InvitationId is { } invitationId
                ? invitationId
                : throw new ProtonToolInputException($"{who} has no invitation waiting.");
        }
        else
        {
            target = contact.Status == PortalClientStatus.Active && contact.AccessId is { } accessId
                ? accessId
                : throw new ProtonToolInputException($"{who} has no access to this project to end.");
        }

        var preview = cancel
            ? new ProtonPreview($"Cancel {who}'s invitation to {project.Title}",
                [new ProtonChange("Invitation", "Waiting", "Cancelled")],
                "The emailed link stops working. They can be invited again.",
                Confirm: ProtonConfirm.Typed, Phrase: who, Reach: "Affects 1 person outside the company.")
            : new ProtonPreview($"End {who}'s access to the client portal of {project.Title}",
                [new ProtonChange("Access", "Can open the portal and write in the discussion", "Ended")],
                "The project disappears from their portal at once, and they are signed out everywhere if it was their last project. " +
                "They can only get access back by being invited again.",
                Confirm: ProtonConfirm.Typed, Phrase: who, Reach: "Affects 1 person outside the company.");
        return (new PortalEndAccessArgs(project.Id, input.Step, contact.Email, target), preview, ProjectText.Ref(project));
    }

    protected override object? RequestOf(PortalEndAccessArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(PortalEndAccessArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var contact = PortalText.Contact(await access.ListAsync(args.ProjectId, cancellationToken), args.Email);
        var cancel = args.Step == ProtonEndAccessStep.CancelInvitation;
        if (cancel ? contact.InvitationId != args.TargetId || contact.Status != PortalClientStatus.Invited : contact.AccessId != args.TargetId || contact.Status != PortalClientStatus.Active)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, $"{PortalText.Who(contact)}'s access changed since Proton proposed this. Ask again.");
        }

        if (cancel)
        {
            await access.RevokeInvitationAsync(args.ProjectId, args.TargetId, cancellationToken);
            return new ProtonActionDone($"Cancelled {PortalText.Who(contact)}'s invitation.", "project", args.ProjectId, []);
        }

        await access.RevokeAccessAsync(args.ProjectId, args.TargetId, cancellationToken);
        return new ProtonActionDone($"Ended {PortalText.Who(contact)}'s access to the client portal.", "project", args.ProjectId, []);
    }
}

internal static class PortalText
{
    /// <summary>The client contact an address names, as the project's client portal lists them; not found when it isn't listed.</summary>
    public static PortalClientResponse Contact(PortalClientsResponse clients, string email)
    {
        var normalized = EmailAddress.Normalize(email);
        return clients.Clients.FirstOrDefault(c => c.Email == normalized)
               ?? throw new ProtonToolInputException(
                   $"{normalized} isn't one of this project's client contacts. get_portal_status lists them; an address is added to the client's contacts first.");
    }

    /// <summary>Their name once they have set up an account, otherwise their address.</summary>
    public static string Who(PortalClientResponse contact) => contact.Name ?? contact.Email;

    /// <summary>
    /// The address an invitation goes to comes from the user, never from the data: they typed it or named the person,
    /// or the client has only one email contact, so there is no one else it could be.
    /// </summary>
    public static void RequireNamed(PortalClientsResponse clients, PortalClientResponse contact, ProtonToolContext context)
    {
        var named = context.TypedText.Contains(contact.Email, StringComparison.OrdinalIgnoreCase)
                    || (contact.Name is { } name && MessageText.Named(name, context.TypedText))
                    || clients.Clients.Count(c => c.IsContact) == 1;
        if (!named)
        {
            throw new ProtonToolInputException("Use a client contact only when the user typed their address or named them in this conversation: ask who it is for.");
        }
    }
}
