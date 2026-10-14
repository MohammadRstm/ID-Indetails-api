using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Auth;
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
/// The client's side of the portal. Follows the staff sign-in (generic errors, a dummy hash for unknown emails, rotating
/// refresh tokens with reuse detection) and adds a per-account pause after repeated wrong passwords.
/// </summary>
public sealed class PortalAuthService(
    IPortalAccountRepository accounts,
    IPortalInvitationRepository invitations,
    IPortalAccessRepository access,
    IPortalSessionRepository sessionTokens,
    IPortalSessionService sessions,
    IPortalPasswordService passwords,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IClock clock,
    IOptions<PortalOptions> portalOptions,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<PortalAuthService> logger,
    ILoggerFactory loggers) : IPortalAuthService
{
    private const string InvalidCredentialsMessage = "Email or password is incorrect. After several wrong attempts, sign-in pauses for a few minutes.";
    private const string SessionExpiredMessage = "Your session has expired. Please sign in again.";
    private const string InvalidResetMessage = "This reset link is invalid or has expired. Ask for a new one.";

    private readonly ILogger _security = loggers.CreateLogger(SecurityLog.Category);
    private PortalOptions Options => portalOptions.Value;

    public async Task<PortalInvitationPreviewResponse> PreviewInvitationAsync(
        PortalInvitationTokenRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await UsableInvitationAsync(request.Token, cancellationToken);
        return new PortalInvitationPreviewResponse
        {
            Email = invitation.Account.Email,
            ProjectTitle = invitation.Project.Title,
            ExpiresAt = invitation.ExpiresAt,
        };
    }

    public async Task<PortalAuthSession> AcceptInvitationAsync(AcceptPortalInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await UsableInvitationAsync(request.Token, cancellationToken);
        var account = invitation.Account;
        var now = clock.UtcNow;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // The account's invitations are settled by one acceptance at a time, with no invitation or resend meanwhile, so
        // two of its links used at once can't claim each other's invitations.
        await accounts.LockAsync(account.Id, cancellationToken);

        // Atomic claim: if the same link is submitted twice at once, only one succeeds.
        if (!await invitations.TryAcceptAsync(invitation.Id, now, cancellationToken))
        {
            throw InvitationRefused(PortalInvitationState.Used);
        }

        account.FirstName = request.FirstName.Trim();
        account.LastName = request.LastName.Trim();
        account.PasswordHash = passwords.Hash(account, request.Password);
        account.Status = PortalAccountStatus.Active;
        account.ActivatedAt = now;
        account.LastSeenAt = now;
        account.UpdatedAt = now;

        // Other projects the client was invited to before setting up the account open too: each was given by staff, and
        // is checked again like the link's own. Each project opens once; every other pending invitation is cancelled,
        // so none of the account's links stays usable.
        var accepted = new List<PortalInvitation> { invitation };
        foreach (var other in await invitations.PendingForAccountAsync(account.Id, now, cancellationToken))
        {
            var opens = accepted.All(a => a.ProjectId != other.ProjectId)
                && await CanAcceptAsync(account.Email, other.ProjectId, cancellationToken);
            if (!opens)
            {
                await invitations.TryRevokeAsync(other.Id, now, cancellationToken);
            }
            else if (await invitations.TryAcceptAsync(other.Id, now, cancellationToken))
            {
                accepted.Add(other);
            }
        }

        foreach (var used in accepted)
        {
            await GrantAsync(account, used, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var session = await sessions.StartAsync(account, Guid.NewGuid(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return session;
    }

    public async Task<PortalAuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var account = await accounts.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);

        // Unknown emails, accounts not set up yet and paused accounts all get the same answer and the same timing.
        if (account is not { Status: PortalAccountStatus.Active } || PortalRules.IsLocked(account.LockedUntil, now))
        {
            passwords.VerifyAgainstDummy(request.Password);
            if (account is not null && PortalRules.IsLocked(account.LockedUntil, now))
            {
                _security.LogWarning("Portal sign-in refused for paused account {AccountId}.", account.Id);
            }

            throw new UnauthenticatedException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        var check = passwords.Verify(account, request.Password);
        if (check == PasswordCheck.Failed)
        {
            // Counted in the database, so wrong passwords sent at once all count.
            var paused = await accounts.CountWrongPasswordAsync(
                account.Id, Options.LockoutAttempts, now.AddMinutes(Options.LockoutMinutes), cancellationToken);
            if (paused)
            {
                _security.LogWarning("Portal account {AccountId} paused after repeated wrong passwords.", account.Id);
            }

            throw new UnauthenticatedException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            account.PasswordHash = passwords.Hash(account, request.Password);
        }

        account.FailedSignIns = 0;
        account.LockedUntil = null;
        account.LastSeenAt = now;
        var session = await sessions.StartAsync(account, Guid.NewGuid(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<PortalAuthSession> RefreshAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            throw SessionExpired();
        }

        var now = clock.UtcNow;
        var presented = await sessionTokens.GetRefreshTokenAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken)
            ?? throw SessionExpired();

        if (presented.RevokedAt is not null)
        {
            // A rotated token presented again after the grace: someone else holds it. Every session of the account ends.
            if (presented.ReplacedById is not null && !RefreshTokenRules.WithinReuseGrace(presented.RevokedAt.Value, now))
            {
                _security.LogWarning(
                    "Portal refresh token {TokenId} of account {AccountId} was presented after rotation; ending every session.",
                    presented.Id, presented.AccountId);
                await EndEverySessionAsync(presented.AccountId, now, cancellationToken);
            }

            throw SessionExpired();
        }

        if (presented.ExpiresAt <= now)
        {
            throw SessionExpired();
        }

        var account = await accounts.GetByIdAsync(presented.AccountId, cancellationToken);
        if (account is not { Status: PortalAccountStatus.Active })
        {
            await sessionTokens.RevokeAllForAccountAsync(presented.AccountId, now, cancellationToken);
            throw SessionExpired();
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (!await sessionTokens.TryRevokeAsync(presented.Id, now, RevokeReason.Rotated, cancellationToken))
        {
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenReused, SessionExpiredMessage);
        }

        account.LastSeenAt = now;
        var session = await sessions.StartAsync(account, presented.SessionId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sessionTokens.SetReplacedByAsync(presented.Id, session.StoredRefreshToken.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return session;
    }

    public async Task LogoutAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            return;
        }

        var token = await sessionTokens.GetRefreshTokenAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken);
        if (token is { RevokedAt: null })
        {
            await sessionTokens.TryRevokeAsync(token.Id, clock.UtcNow, RevokeReason.SignedOut, cancellationToken);
        }
    }

    public async Task<PortalAccountResponse> GetAccountAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByIdAsync(accountId, cancellationToken);
        return account is { Status: PortalAccountStatus.Active }
            ? await sessions.DescribeAsync(account, cancellationToken)
            : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, SessionExpiredMessage);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);
        if (account is not { Status: PortalAccountStatus.Active })
        {
            return;
        }

        var now = clock.UtcNow;
        var token = SecureTokens.NewToken();
        var expiresAt = now.AddMinutes(Options.PasswordResetExpirationMinutes);

        // Only the latest link works.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await sessionTokens.ExpirePendingResetsAsync(account.Id, now, cancellationToken);
        sessionTokens.Add(new PortalPasswordResetToken
        {
            AccountId = account.Id,
            TokenHash = SecureTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Not awaited, so the answer takes as long whether or not the email has an account. A failure is only logged.
        _ = SendResetAsync(
            AccountEmails.PasswordReset(
                emailOptions.Value.ProductName,
                account.Email,
                account.FirstName,
                frontendOptions.Value.PortalResetPasswordUrl(token),
                expiresAt),
            account.Id);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var reset = await sessionTokens.GetResetAsync(SecureTokens.Hash(request.Token), cancellationToken);
        if (reset is null || reset.UsedAt is not null || reset.ExpiresAt <= now || reset.Account.Status != PortalAccountStatus.Active)
        {
            throw new NotFoundException(ErrorCodes.PasswordResetInvalid, InvalidResetMessage);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (!await sessionTokens.TryUseResetAsync(reset.Id, now, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.PasswordResetInvalid, InvalidResetMessage);
        }

        var account = reset.Account;
        account.PasswordHash = passwords.Hash(account, request.Password);
        account.FailedSignIns = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;
        // signed out everywhere: refresh tokens and the access tokens already handed out
        account.SessionsValidFrom = now;

        await sessionTokens.ExpirePendingResetsAsync(account.Id, now, cancellationToken);
        await sessionTokens.RevokeAllForAccountAsync(account.Id, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The invitation behind the link, if it can still be used; otherwise a clear reason.</summary>
    private async Task<PortalInvitation> UsableInvitationAsync(string token, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByTokenHashAsync(SecureTokens.Hash(token), cancellationToken)
            ?? throw new NotFoundException(
                ErrorCodes.PortalInvitationInvalid,
                "This invitation link isn't valid. Check that you opened the whole link from the email, or ask your project contact for a new invitation.");

        var state = PortalRules.StateOf(
            invitation.AcceptedAt, invitation.RevokedAt, invitation.ExpiresAt, invitation.Account.Status, clock.UtcNow);
        if (state == PortalInvitationState.Usable && !await CanAcceptAsync(invitation.Account.Email, invitation.ProjectId, cancellationToken))
        {
            // The project no longer takes this client (it or its client was archived, or the address is no longer a contact).
            state = PortalInvitationState.Revoked;
        }

        return state == PortalInvitationState.Usable ? invitation : throw InvitationRefused(state);
    }

    /// <summary>Whether the project would still take the address now: see <see cref="PortalRules.CanAccept"/>.</summary>
    private async Task<bool> CanAcceptAsync(string email, long projectId, CancellationToken cancellationToken)
        => await access.GetProjectAsync(projectId, cancellationToken) is { } project && PortalRules.CanAccept(project, email);

    private static BusinessRuleException InvitationRefused(PortalInvitationState state) => state switch
    {
        PortalInvitationState.Expired => new(
            ErrorCodes.PortalInvitationExpired, "This invitation has expired. Ask your project contact to send you a new one."),
        PortalInvitationState.Revoked => new(
            ErrorCodes.PortalInvitationRevoked, "This invitation was cancelled. Ask your project contact if you should have access."),
        _ => new(ErrorCodes.PortalInvitationUsed, "This invitation was already used. Sign in with your email address and password."),
    };

    /// <summary>Gives the account access to the invitation's project, reusing an ended access row.</summary>
    private async Task GrantAsync(PortalAccount account, PortalInvitation invitation, DateTime now, CancellationToken cancellationToken)
    {
        var row = await access.GetAsync(account.Id, invitation.ProjectId, cancellationToken);
        if (row is null)
        {
            access.Add(new PortalProjectAccess
            {
                AccountId = account.Id,
                ProjectId = invitation.ProjectId,
                GrantedAt = now,
                GrantedById = invitation.InvitedById,
            });
        }
        else if (row.RevokedAt is not null)
        {
            row.GrantedAt = now;
            row.GrantedById = invitation.InvitedById;
            row.RevokedAt = null;
            row.RevokedById = null;
        }

        activity.Add(new ActivityEvent
        {
            DomainCode = invitation.Project.DomainCode,
            Kind = ActivityEventKinds.PortalInvitationAccepted,
            OccurredAt = now,
            ProjectId = invitation.ProjectId,
            ClientId = invitation.Project.ClientId,
            SubjectType = "portal_account",
            SubjectId = account.Id,
            Payload = JsonSerializer.Serialize(new { email = account.Email }),
        });
    }

    private async Task EndEverySessionAsync(long accountId, DateTime now, CancellationToken cancellationToken)
    {
        await sessionTokens.RevokeAllForAccountAsync(accountId, now, cancellationToken);
        if (await accounts.GetByIdAsync(accountId, cancellationToken) is { } account)
        {
            account.SessionsValidFrom = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SendResetAsync(EmailMessage message, long accountId)
    {
        try
        {
            await emailSender.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the portal password reset email for account {AccountId}.", accountId);
        }
    }

    private static UnauthenticatedException SessionExpired() => new(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
}
