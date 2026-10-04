using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;

namespace ID_Indetails.api.Services.Auth.Implementation;

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordResetRepository resets,
    IPermissionService permissions,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    ISessionService sessions,
    IClock clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentialsMessage = "Email or password is incorrect.";
    private const string SessionExpiredMessage = "Your session has expired. Please sign in again.";

    public async Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);

        // Unknown email, invited-but-not-signed-up and deactivated accounts all get the same
        // answer and the same timing, so the response never reveals which emails exist.
        if (user is null || user.Status != UserStatus.Active)
        {
            passwords.VerifyAgainstDummy(request.Password);
            throw new UnauthenticatedException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        var check = passwords.Verify(user, request.Password);
        if (check == PasswordCheck.Failed)
        {
            throw new UnauthenticatedException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        // Only said once the password is right, so it never tells a stranger anything about the account.
        if (user.SessionsRevokedAt is not null)
        {
            throw new ForbiddenException(ErrorCodes.SignInSuspended, SessionRules.SuspendedMessage);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.PasswordHash = passwords.Hash(user, request.Password);
        }

        user.LastSeenAt = clock.UtcNow;

        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
        }

        var now = clock.UtcNow;
        var presented = await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken)
            ?? throw new UnauthenticatedException(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);

        if (presented.RevokedAt is not null)
        {
            if (presented.ReplacedById is not null)
            {
                await SignOutEverywhereAfterReuseAsync(presented, now, cancellationToken);
                permissions.Forget(presented.UserId);
            }

            throw new UnauthenticatedException(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
        }

        if (presented.ExpiresAt <= now)
        {
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
        }

        var user = await users.GetByIdAsync(presented.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(presented.UserId, now, cancellationToken);
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
        }

        if (user.SessionsRevokedAt is not null)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
            throw new UnauthenticatedException(ErrorCodes.SignInSuspended, SessionRules.SuspendedMessage);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Claim the token atomically. If another request rotated it a moment earlier, this
        // presentation is a reuse.
        if (!await refreshTokens.TryRevokeAsync(presented.Id, now, cancellationToken))
        {
            await SignOutEverywhereAfterReuseAsync(presented, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            permissions.Forget(presented.UserId);
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenReused, SessionExpiredMessage);
        }

        user.LastSeenAt = now;
        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await refreshTokens.SetReplacedByAsync(presented.Id, session.StoredRefreshToken.Id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return session;
    }

    public async Task LogoutAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            return;
        }

        var token = await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken);
        if (token is { RevokedAt: null })
        {
            await refreshTokens.TryRevokeAsync(token.Id, clock.UtcNow, cancellationToken);
        }
    }

    public async Task<AuthSession> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);

        if (passwords.Verify(user, request.CurrentPassword) == PasswordCheck.Failed)
        {
            throw new RequestValidationException(
                ErrorCodes.CurrentPasswordIncorrect,
                "currentPassword",
                "The current password is incorrect.");
        }

        var now = clock.UtcNow;
        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = now;
        // every other device is signed out, the access tokens it holds included; this one gets a new session
        user.SessionsValidFrom = SessionRules.CutOffKeepingThisSecond(now);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
        await resets.ExpirePendingForUserAsync(user.Id, now, cancellationToken);
        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return session;
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
        => await sessions.DescribeAsync(await GetActiveUserAsync(userId, cancellationToken), cancellationToken);

    private async Task<User> GetActiveUserAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { Status: UserStatus.Active }
            ? user
            : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, SessionExpiredMessage);
    }

    private async Task SignOutEverywhereAfterReuseAsync(RefreshToken token, DateTime now, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Refresh token {TokenId} of user {UserId} was presented after rotation; revoking all sessions.",
            token.Id, token.UserId);

        await refreshTokens.RevokeAllActiveForUserAsync(token.UserId, now, cancellationToken);

        // whoever replayed the token may hold an access token from it: those stop working too
        if (await users.GetByIdAsync(token.UserId, cancellationToken) is { } user)
        {
            user.SessionsValidFrom = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
