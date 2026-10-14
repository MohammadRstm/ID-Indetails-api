using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// Staff managing who of a Design project's client can follow it in the client portal. Only the client's own email
/// contacts can be invited; an invitation is a one-time link that expires; access is given per project and can be ended.
/// Execution projects have no portal and read as not found.
/// </summary>
public sealed class PortalAccessService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPortalAccountRepository accounts,
    IPortalInvitationRepository invitations,
    IPortalAccessRepository access,
    IPortalSessionRepository sessionTokens,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IClock clock,
    IOptions<PortalOptions> portalOptions,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<PortalAccessService> logger) : IPortalAccessService
{
    private const string ProjectNotFound = "Project not found.";

    public async Task<PortalClientsResponse> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<PortalClientsResponse> InviteAsync(long projectId, InvitePortalClientRequest request, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalRules.CheckInvitable(project));

        var email = EmailAddress.Normalize(request.Email);
        if (!PortalRules.IsClientContact(project.EmailContacts, email))
        {
            throw new RequestValidationException(
                ErrorCodes.PortalEmailNotClientContact,
                "email",
                "Only the client's own email addresses can be invited. Add it to the client's contacts first.");
        }

        var now = clock.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Locked, so two invitations at once can't both find nothing pending and send two links.
        var account = await accounts.GetByEmailForUpdateAsync(email, cancellationToken);

        if (account is { Status: PortalAccountStatus.Active })
        {
            // Already set up: the project opens at once, and the client is told.
            var row = await access.GetAsync(account.Id, project.Id, cancellationToken);
            if (row is { RevokedAt: null })
            {
                throw new ConflictException(ErrorCodes.PortalAlreadyInvited, "This client already has access to the project.");
            }

            Grant(row, account.Id, project.Id, now);
            AddEvent(ActivityEventKinds.PortalAccessGranted, project, account, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await SendAsync(
                AccountEmails.PortalAccessGranted(
                    emailOptions.Value.ProductName, account.Email, account.FirstName, InviterName, project.Title, frontendOptions.Value.PortalLoginUrl),
                account.Id, cancellationToken);
        }
        else
        {
            if (account is null)
            {
                account = new PortalAccount
                {
                    Email = email,
                    Status = PortalAccountStatus.Invited,
                    CreatedAt = now,
                    CreatedById = currentUser.Id,
                };
                accounts.Add(account);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else if (await invitations.HasPendingAsync(account.Id, project.Id, now, cancellationToken))
            {
                throw new ConflictException(
                    ErrorCodes.PortalAlreadyInvited, "This client already has an invitation waiting. Resend it instead.");
            }

            await StageAndSendInvitationAsync(project, account, ActivityEventKinds.PortalClientInvited, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<PortalClientsResponse> ResendAsync(long projectId, long invitationId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalRules.CheckInvitable(project));
        var invitation = await invitations.GetAsync(invitationId, cancellationToken);
        if (invitation is null || invitation.ProjectId != project.Id)
        {
            throw new NotFoundException("Invitation not found.");
        }

        if (invitation.Account.Status != PortalAccountStatus.Invited || invitation.AcceptedAt is not null)
        {
            throw new BusinessRuleException(ErrorCodes.PortalInvitationUsed, "This client has already set up their account.");
        }

        if (!PortalRules.IsClientContact(project.EmailContacts, invitation.Account.Email))
        {
            throw new BusinessRuleException(
                ErrorCodes.PortalEmailNotClientContact, "This address is no longer one of the client's contacts, so it can't be invited.");
        }

        var now = clock.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Locked as when inviting, so two resends at once leave one link.
        await accounts.LockAsync(invitation.AccountId, cancellationToken);
        await invitations.RevokePendingAsync(invitation.AccountId, project.Id, now, cancellationToken);
        await StageAndSendInvitationAsync(project, invitation.Account, ActivityEventKinds.PortalInvitationResent, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<PortalClientsResponse> RevokeInvitationAsync(long projectId, long invitationId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        var invitation = await invitations.GetAsync(invitationId, cancellationToken);
        if (invitation is null || invitation.ProjectId != project.Id)
        {
            throw new NotFoundException("Invitation not found.");
        }

        var now = clock.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Claimed like accepting: if the client accepts at the same moment, only one of the two happens.
        if (!await invitations.TryRevokeAsync(invitation.Id, now, cancellationToken))
        {
            throw new BusinessRuleException(ErrorCodes.PortalInvitationUsed, "This invitation was already used or cancelled.");
        }

        AddEvent(ActivityEventKinds.PortalInvitationRevoked, project, invitation.Account, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<PortalClientsResponse> RevokeAccessAsync(long projectId, long accessId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        var row = await access.GetByIdAsync(accessId, cancellationToken);
        if (row is null || row.ProjectId != project.Id)
        {
            throw new NotFoundException("Access not found.");
        }

        if (row.RevokedAt is not null)
        {
            throw new BusinessRuleException(ErrorCodes.PortalAccessAlreadyEnded, "This client's access was already ended.");
        }

        var now = clock.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await EndAccessAsync(row, project.Id, now, cancellationToken);
        AddEvent(ActivityEventKinds.PortalAccessRevoked, project, row.Account, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(project, cancellationToken);
    }

    public async Task EndAllForProjectAsync(
        long projectId, string domainCode, long? previousClientId, long actorId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await invitations.RevokePendingForProjectAsync(projectId, now, cancellationToken);
        foreach (var row in await access.LiveForProjectAsync(projectId, cancellationToken))
        {
            row.RevokedAt = now;
            row.RevokedById = actorId;
            await EndSessionsIfNoAccessLeftAsync(row.Account, projectId, now, cancellationToken);
            activity.Add(Event(ActivityEventKinds.PortalAccessRevoked, domainCode, projectId, previousClientId, row.Account, actorId, now));
        }
    }

    public async Task EndAllForClientAsync(long clientId, string domainCode, long actorId, CancellationToken cancellationToken = default)
    {
        foreach (var projectId in await access.ProjectIdsOfClientAsync(clientId, cancellationToken))
        {
            await EndAllForProjectAsync(projectId, domainCode, clientId, actorId, cancellationToken);

            // Saved project by project, so the next one sees which accounts have no project left and signs them out.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>A Design project the caller manages client access on. Anything else reads as not found.</summary>
    private async Task<PortalProjectFacts> VisibleProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ClientAccessManage);
        var project = await access.GetProjectAsync(projectId, cancellationToken);
        return project is not null && project.DomainCode == DomainCodes.Design && actor.CanSee(project.DomainCode)
            ? project
            : throw new NotFoundException(ProjectNotFound);
    }

    private async Task StageAndSendInvitationAsync(
        PortalProjectFacts project, PortalAccount account, string kind, DateTime now, CancellationToken cancellationToken)
    {
        var token = SecureTokens.NewToken();
        var expiresAt = now.AddHours(portalOptions.Value.InvitationExpirationHours);
        invitations.Add(new PortalInvitation
        {
            AccountId = account.Id,
            ProjectId = project.Id,
            TokenHash = SecureTokens.Hash(token),
            SentAt = now,
            ExpiresAt = expiresAt,
            InvitedById = currentUser.Id,
        });
        AddEvent(kind, project, account, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Sent inside the transaction: if the email can't be delivered, nothing is saved and the inviter can try again.
        await SendAsync(
            AccountEmails.PortalInvitation(
                emailOptions.Value.ProductName, account.Email, InviterName, project.Title, frontendOptions.Value.PortalInvitationUrl(token), expiresAt),
            account.Id, cancellationToken);
    }

    /// <summary>Ends the access and the account's pending invitations to the project; its sessions end when no project is left.</summary>
    private async Task EndAccessAsync(PortalProjectAccess row, long projectId, DateTime now, CancellationToken cancellationToken)
    {
        row.RevokedAt = now;
        row.RevokedById = currentUser.Id;
        await invitations.RevokePendingAsync(row.AccountId, projectId, now, cancellationToken);
        await EndSessionsIfNoAccessLeftAsync(row.Account, projectId, now, cancellationToken);
    }

    /// <summary>Signs the account out everywhere when <paramref name="endedProjectId"/> was its last project.</summary>
    private async Task EndSessionsIfNoAccessLeftAsync(PortalAccount account, long endedProjectId, DateTime now, CancellationToken cancellationToken)
    {
        if (!await access.HasLiveAccessAsync(account.Id, endedProjectId, cancellationToken))
        {
            account.SessionsValidFrom = now;
            await sessionTokens.RevokeAllForAccountAsync(account.Id, now, cancellationToken);
        }
    }

    private void Grant(PortalProjectAccess? row, long accountId, long projectId, DateTime now)
    {
        if (row is null)
        {
            access.Add(new PortalProjectAccess { AccountId = accountId, ProjectId = projectId, GrantedAt = now, GrantedById = currentUser.Id });
            return;
        }

        row.GrantedAt = now;
        row.GrantedById = currentUser.Id;
        row.RevokedAt = null;
        row.RevokedById = null;
    }

    private async Task<PortalClientsResponse> DescribeAsync(PortalProjectFacts project, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var rows = await access.ForProjectAsync(project.Id, cancellationToken);
        var latest = await invitations.LatestForProjectAsync(project.Id, cancellationToken);

        // The client's contacts first, then anyone who had access or an invitation and is no longer a contact.
        var emails = project.EmailContacts.Select(c => c.Email)
            .Concat(rows.Select(r => r.Account.Email))
            .Distinct()
            .ToList();
        var byEmail = (await accounts.GetByEmailsAsync(emails, cancellationToken)).ToDictionary(a => a.Email);
        var invitedEmails = latest.Select(i => i.AccountId).ToHashSet();
        emails.AddRange(byEmail.Values.Where(a => invitedEmails.Contains(a.Id) && !emails.Contains(a.Email)).Select(a => a.Email));

        var clients = emails.Select(email =>
        {
            var contact = project.EmailContacts.FirstOrDefault(c => c.Email == email);
            var account = byEmail.GetValueOrDefault(email);
            var row = account is null ? null : rows.FirstOrDefault(r => r.AccountId == account.Id);
            var invitation = account is null ? null : latest.FirstOrDefault(i => i.AccountId == account.Id);
            return new PortalClientResponse
            {
                Email = email,
                Label = contact?.Label,
                IsContact = contact is not null,
                Status = PortalRules.StatusOf(
                    account?.Status, row?.RevokedAt, row is not null, invitation?.AcceptedAt, invitation?.RevokedAt, invitation?.ExpiresAt, now),
                AccountId = account?.Id,
                Name = account is { FirstName: { } first } ? $"{first} {account.LastName}".Trim() : null,
                InvitationId = invitation?.Id,
                InvitedAt = invitation?.SentAt,
                InvitationExpiresAt = invitation?.ExpiresAt,
                AccessId = row?.Id,
                AccessGrantedAt = row?.GrantedAt,
                AccessEndedAt = row?.RevokedAt,
                LastSeenAt = account?.LastSeenAt,
            };
        }).ToList();

        return new PortalClientsResponse
        {
            ProjectId = project.Id,
            CannotInviteReason = PortalRules.CheckInvitable(project)?.Message,
            Clients = clients,
        };
    }

    private string InviterName => string.IsNullOrWhiteSpace(currentUser.FullName) ? "Your project team" : currentUser.FullName;

    private void AddEvent(string kind, PortalProjectFacts project, PortalAccount account, DateTime now)
        => activity.Add(Event(kind, project.DomainCode, project.Id, project.ClientId, account, currentUser.Id, now));

    private static ActivityEvent Event(
        string kind, string domainCode, long projectId, long? clientId, PortalAccount account, long actorId, DateTime now) => new()
    {
        DomainCode = domainCode,
        Kind = kind,
        OccurredAt = now,
        ActorId = actorId,
        ProjectId = projectId,
        ClientId = clientId,
        SubjectType = "portal_account",
        SubjectId = account.Id,
        Payload = JsonSerializer.Serialize(new { email = account.Email }),
    };

    private async Task SendAsync(EmailMessage message, long accountId, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send a client portal email to account {AccountId}.", accountId);
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed, "The email to the client could not be sent, so nothing was saved. Please try again.", ex);
        }
    }

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
