using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.DTO.Response.Invitations;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Invitations.Abstraction;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Invitations.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Invitations.Implementation;

public sealed class InvitationService(
    ICurrentUser currentUser,
    IUserRepository users,
    IInvitationRepository invitations,
    ITeamRepository teams,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    ISessionService sessions,
    IPermissionService permissions,
    INotificationSender notifications,
    IEmailSender emailSender,
    IClock clock,
    IOptions<InvitationOptions> invitationOptions,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<InvitationService> logger) : IInvitationService
{
    private const string InvalidLinkMessage = "This invitation link is invalid or has expired. Ask for a new invitation.";

    public async Task<InvitationResponse> CreateAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var inviter = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var decision = InvitationPolicy.Evaluate(inviter, request.Role, request.DomainCode);
        if (!decision.Allowed)
        {
            throw new ForbiddenException(ErrorCodes.InvitationNotAllowed, decision.DenialReason!);
        }

        if (await DefaultsNotHeldAsync(inviter, request.Role, decision.DomainCode, cancellationToken) is { } notHeld)
        {
            throw new ForbiddenException(ErrorCodes.InvitationNotAllowed, notHeld);
        }

        if (request.Pay is not null && !inviter.Has(PermissionCodes.EmployeePayManage))
        {
            throw new ForbiddenException(ErrorCodes.InvitationNotAllowed, "Setting someone's pay needs the permission to change pay.");
        }

        if (request.Pay is { } terms && PayRules.CheckBasis(request.Role, terms.Basis) is { } denial)
        {
            throw denial.ToException();
        }

        if (request.TeamId is { } teamId)
        {
            var team = await teams.GetByIdAsync(teamId, cancellationToken);
            if (team is null || team.DomainCode != decision.DomainCode)
            {
                throw new RequestValidationException(
                    ErrorCodes.TeamNotInDomain, "teamId", "The team does not exist in this domain.");
            }
        }

        var email = EmailAddress.Normalize(request.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException(
                ErrorCodes.EmailAlreadyRegistered,
                "Someone with this email is already registered or invited. Resend their invitation instead.");
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Email = email,
            RoleCode = request.Role,
            DomainCode = decision.DomainCode,
            TeamId = request.TeamId,
            Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
            Status = UserStatus.Invited,
            CreatedAt = now,
            CreatedById = currentUser.Id,
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Pay is { } pay)
        {
            users.AddPayHistory(new UserPayHistory
            {
                UserId = user.Id,
                EffectiveFrom = pay.EffectiveFrom ?? clock.CompanyToday,
                Basis = pay.Basis,
                Amount = pay.Amount,
                Currency = pay.Currency.ToUpperInvariant(),
                HourlyCostRate = pay.Basis == PayBasis.Hourly ? pay.Amount : 0,
                Note = "Set at invitation",
                CreatedAt = now,
                CreatedById = currentUser.Id,
            });
        }

        var (token, expiresAt) = StageInvitation(user.Id, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Sent inside the transaction: if the email cannot be delivered, nothing is saved and
        // the inviter can simply try again.
        await SendInvitationEmailAsync(user, token, expiresAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(user, expiresAt);
    }

    public async Task<InvitationResponse> ResendAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        var inviter = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);

        // Someone the caller could not have invited reads as not found (no cross-domain peeking).
        var decision = user is null
            ? null
            : InvitationPolicy.Evaluate(inviter, user.RoleCode, user.DomainCode);
        if (user is null || decision is not { Allowed: true }
            || await DefaultsNotHeldAsync(inviter, user.RoleCode, user.DomainCode, cancellationToken) is not null)
        {
            throw new NotFoundException("User not found.");
        }

        if (user.Status != UserStatus.Invited)
        {
            throw new BusinessRuleException(ErrorCodes.UserNotInvited, "This person has already signed up or is deactivated.");
        }

        var now = clock.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await invitations.RevokePendingForUserAsync(user.Id, now, cancellationToken);
        var (token, expiresAt) = StageInvitation(user.Id, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendInvitationEmailAsync(user, token, expiresAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(user, expiresAt);
    }

    public async Task<InvitationPreviewResponse> PreviewAsync(InvitationTokenRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await GetUsableInvitationAsync(request.Token, cancellationToken);

        return new InvitationPreviewResponse
        {
            Email = invitation.User.Email,
            Role = invitation.User.RoleCode,
            Domain = invitation.User.DomainCode,
            Team = invitation.User.Team?.Name,
            Position = invitation.User.Position,
            ExpiresAt = invitation.ExpiresAt,
        };
    }

    public async Task<AuthSession> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await GetUsableInvitationAsync(request.Token, cancellationToken);
        var user = invitation.User;
        var now = clock.UtcNow;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Atomic claim: if the same link is submitted twice at once, only one succeeds.
        if (!await invitations.TryAcceptAsync(invitation.Id, now, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.InvitationInvalid, InvalidLinkMessage);
        }

        await invitations.RevokePendingForUserAsync(user.Id, now, cancellationToken);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.PasswordHash = passwords.Hash(user, request.Password);
        user.Status = UserStatus.Active;
        user.JoinedOn = clock.CompanyToday;
        user.EmploymentPeriods.Add(new EmploymentPeriod { StartedOn = clock.CompanyToday, CreatedAt = now });
        user.LastSeenAt = now;
        user.UpdatedAt = now;

        await NotifyAcceptedAsync(invitation, cancellationToken);
        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return session;
    }

    /// <summary>Tells whoever sent the invitation, and whoever invites people into that domain.</summary>
    private async Task<string?> DefaultsNotHeldAsync(UserAccess inviter, string role, string? domain, CancellationToken cancellationToken)
    {
        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        return InvitationPolicy.CheckDefaultsHeld(inviter, EffectivePermissions.RoleDefaults(role, domain, grants[role]));
    }

    private async Task NotifyAcceptedAsync(UserInvitation invitation, CancellationToken cancellationToken)
    {
        var user = invitation.User;
        var inviters = user.DomainCode is { } domain
            ? await notifications.HoldersAsync(domain, PermissionCodes.EmployeeInvite, cancellationToken)
            : [];
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.InvitationAccepted, user.Id, user.DomainCode, null, "user", user.Id,
                new { role = user.RoleCode, domain = user.DomainCode }),
            invitation.InvitedById is { } inviter ? inviters.Append(inviter) : inviters,
            cancellationToken);
    }

    private async Task<UserInvitation> GetUsableInvitationAsync(string token, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByTokenHashAsync(SecureTokens.Hash(token), cancellationToken);

        var usable = invitation is not null
            && invitation.AcceptedAt is null
            && invitation.RevokedAt is null
            && invitation.ExpiresAt > clock.UtcNow
            && invitation.User.Status == UserStatus.Invited;

        return usable ? invitation! : throw new NotFoundException(ErrorCodes.InvitationInvalid, InvalidLinkMessage);
    }

    private (string Token, DateTime ExpiresAt) StageInvitation(long userId, DateTime now)
    {
        var token = SecureTokens.NewToken();
        var expiresAt = now.AddDays(invitationOptions.Value.ExpirationDays);

        invitations.Add(new UserInvitation
        {
            UserId = userId,
            TokenHash = SecureTokens.Hash(token),
            SentAt = now,
            ExpiresAt = expiresAt,
            InvitedById = currentUser.Id,
        });

        return (token, expiresAt);
    }

    private async Task SendInvitationEmailAsync(User user, string token, DateTime expiresAt, CancellationToken cancellationToken)
    {
        var message = AccountEmails.Invitation(
            emailOptions.Value.ProductName,
            user.Email,
            string.IsNullOrWhiteSpace(currentUser.FullName) ? "An administrator" : currentUser.FullName,
            RoleLabel(user.RoleCode, user.DomainCode),
            frontendOptions.Value.AcceptInvitationUrl(token),
            expiresAt);

        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the invitation email for user {UserId}.", user.Id);
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed,
                "The invitation email could not be sent, so nothing was saved. Please try again.",
                ex);
        }
    }

    private static string RoleLabel(string role, string? domain) => role switch
    {
        RoleCodes.SuperAdmin => "a super admin",
        RoleCodes.Admin => $"an admin of {DomainLabel(domain)}",
        _ => $"a member of {DomainLabel(domain)}",
    };

    private static string DomainLabel(string? domain) => domain switch
    {
        DomainCodes.Design => "Design",
        DomainCodes.Execution => "Execution",
        DomainCodes.Website => "the Website",
        _ => "the company",
    };

    private static InvitationResponse ToResponse(User user, DateTime expiresAt) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        Role = user.RoleCode,
        Domain = user.DomainCode,
        ExpiresAt = expiresAt,
    };
}
