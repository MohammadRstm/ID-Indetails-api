using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Invitations.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Invitations.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Employees.Implementation;

/// <summary>
/// Changing a sign-in address (<c>employee:change_email</c>), which takes effect at once:
/// <list type="bullet">
/// <item>the person's sessions end: someone else's from their next request, as when their sessions are ended; your own
/// keeps only the one you are using;</item>
/// <item>the new address is told inside the transaction, so nothing changes if it can't be reached; the old one is told
/// afterwards, if it still can be;</item>
/// <item>someone still invited isn't told: the link sent to the old address stops working, and the invitation goes again
/// to the new one.</item>
/// </list>
/// </summary>
public sealed class SignInEmailService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IInvitationRepository invitations,
    IInvitationService invitationService,
    IEmployeeService employees,
    IActivityEventRepository activity,
    IPasswordService passwords,
    ISessionService sessions,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<EmailOptions> emailOptions,
    IOptions<FrontendOptions> frontendOptions,
    ILogger<SignInEmailService> logger) : ISignInEmailService
{
    public async Task<EmployeeResponse> ChangeAsync(long userId, ChangeEmailRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        User user;
        string oldEmail;

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            user = await users.GetByIdForUpdateAsync(userId, cancellationToken) is { } found && EmployeeRules.CanSee(actor, found.DomainCode)
                ? found
                : throw new NotFoundException("Employee not found.");
            Throw(EmployeeRules.CheckEmailChange(actor, new EmployeeRules.Target(user.Id, user.RoleCode, user.DomainCode, user.Status)));
            oldEmail = await SetAsync(user, request.Email, actor.UserId, cancellationToken);

            var now = clock.UtcNow;
            if (user.Status == UserStatus.Invited)
            {
                await invitations.RevokePendingForUserAsync(user.Id, now, cancellationToken);
            }
            else
            {
                user.SessionsValidFrom = now;
                await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (user.Status != UserStatus.Invited)
            {
                await TellNewAddressAsync(user, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            permissions.Forget(user.Id);
        }

        if (user.Status == UserStatus.Invited)
        {
            await ResendInvitationAsync(user.Id, cancellationToken);
        }
        else
        {
            await TellOldAddressAsync(user, oldEmail);
        }

        return await employees.GetAsync(user.Id, cancellationToken);
    }

    public async Task<AuthSession> ChangeMineAsync(ChangeMyEmailRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await users.GetByIdForUpdateAsync(currentUser.Id, cancellationToken) is { Status: UserStatus.Active } self
            ? self
            : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "Your session has expired. Sign in again.");
        if (passwords.Verify(user, request.CurrentPassword) == PasswordCheck.Failed)
        {
            throw new RequestValidationException(ErrorCodes.CurrentPasswordIncorrect, "currentPassword", "The current password is incorrect.");
        }

        var oldEmail = await SetAsync(user, request.Email, user.Id, cancellationToken);
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, clock.UtcNow, cancellationToken);
        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await TellNewAddressAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await TellOldAddressAsync(user, oldEmail);
        return session;
    }

    /// <summary>Sets the new address and records the change; returns the old address.</summary>
    private async Task<string> SetAsync(User user, string newEmail, long actorId, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Normalize(newEmail);
        if (email == user.Email)
        {
            throw new BusinessRuleException(ErrorCodes.EmailUnchanged, "That is already the sign-in address.");
        }

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.EmailAlreadyRegistered, "Someone else is already registered or invited with this email.");
        }

        var oldEmail = user.Email;
        user.Email = email;
        user.UpdatedAt = clock.UtcNow;
        // No addresses in the event: the activity feed is read more widely than the people list.
        activity.Add(new ActivityEvent
        {
            DomainCode = user.DomainCode,
            Kind = ActivityEventKinds.EmailChanged,
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            SubjectType = "user",
            SubjectId = user.Id,
        });
        return oldEmail;
    }

    private async Task TellNewAddressAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(
                AccountEmails.SignInAddressSet(emailOptions.Value.ProductName, user.Email, user.FirstName, frontendOptions.Value.LoginUrl),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not email the new sign-in address of user {UserId}.", user.Id);
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed,
                "The new address couldn't be emailed, so nothing was changed. Check it and try again.",
                ex);
        }
    }

    /// <summary>After the change is saved, so it is told even if the request is cancelled; a failure only logs.</summary>
    private async Task TellOldAddressAsync(User user, string oldEmail)
    {
        try
        {
            await emailSender.SendAsync(AccountEmails.SignInAddressReplaced(emailOptions.Value.ProductName, oldEmail, user.FirstName));
        }
        catch (Exception ex)
        {
            // Often the reason for the change: the old mailbox no longer works.
            logger.LogWarning(ex, "Could not email the old sign-in address of user {UserId}.", user.Id);
        }
    }

    private async Task ResendInvitationAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            await invitationService.ResendAsync(userId, cancellationToken);
        }
        catch (ServiceUnavailableException ex)
        {
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed,
                "The address was changed, but the invitation couldn't be sent to it. Resend it from their profile.",
                ex);
        }
    }

    private static void Throw(Business.Common.RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
