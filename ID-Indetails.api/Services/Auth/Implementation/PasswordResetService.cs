using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Auth.Implementation;

/// <summary>
/// Asking for a reset link always gets the same answer, so it never reveals whether an email belongs to anyone (see
/// <see cref="PasswordResetRules"/>). Only the link's hash is stored.
/// </summary>
public sealed class PasswordResetService(
    IUserRepository users,
    IPasswordResetRepository resets,
    IRefreshTokenRepository refreshTokens,
    IPermissionService permissions,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    IEmailSender emailSender,
    IClock clock,
    IOptions<PasswordResetOptions> resetOptions,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private const string InvalidLinkMessage = "This reset link is invalid or has expired. Ask for a new one.";

    public async Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);
        if (user is null || !PasswordResetRules.CanReset(user.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        var token = SecureTokens.NewToken();
        var expiresAt = now.AddMinutes(resetOptions.Value.ExpirationMinutes);

        // Only the latest link works.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await resets.ExpirePendingForUserAsync(user.Id, now, cancellationToken);
        resets.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = SecureTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Not awaited, so sending doesn't make the answer slower when the email belongs to someone. A failure is only
        // logged: the person can ask again.
        _ = SendAsync(
            AccountEmails.PasswordReset(
                emailOptions.Value.ProductName,
                user.Email,
                user.FirstName,
                frontendOptions.Value.ResetPasswordUrl(token),
                expiresAt),
            user.Id);
    }

    public async Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var reset = await resets.GetByTokenHashAsync(SecureTokens.Hash(request.Token), cancellationToken);
        if (reset is null || !PasswordResetRules.IsUsable(reset.UsedAt, reset.ExpiresAt, reset.User.Status, now))
        {
            throw new NotFoundException(ErrorCodes.PasswordResetInvalid, InvalidLinkMessage);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Atomic claim: if the same link is submitted twice at once, only one succeeds.
        if (!await resets.TryUseAsync(reset.Id, now, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.PasswordResetInvalid, InvalidLinkMessage);
        }

        var user = reset.User;
        user.PasswordHash = passwords.Hash(user, request.Password);
        user.MustChangePassword = false;
        user.UpdatedAt = now;
        // signed out everywhere: refresh tokens and the access tokens already handed out
        user.SessionsValidFrom = now;

        await resets.ExpirePendingForUserAsync(user.Id, now, cancellationToken);
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);
    }

    private async Task SendAsync(EmailMessage message, long userId)
    {
        try
        {
            await emailSender.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the password reset email for user {UserId}.", userId);
        }
    }
}
