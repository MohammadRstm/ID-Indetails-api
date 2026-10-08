using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Push.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Auth.Implementation;

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordResetRepository resets,
    IUserDeviceRepository devices,
    IPermissionService permissions,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    ISessionService sessions,
    IOptions<MobileSessionOptions> mobileOptions,
    IClock clock,
    ILogger<AuthService> logger,
    ILoggerFactory loggers) : IAuthService
{
    private const string InvalidCredentialsMessage = "Email or password is incorrect.";
    private const string SessionExpiredMessage = "Your session has expired. Please sign in again.";

    private readonly ILogger _security = loggers.CreateLogger(SecurityLog.Category);

    public async Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await AuthenticateAsync(request, cancellationToken);
        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            throw SessionExpired();
        }

        var now = clock.UtcNow;
        var presented = await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken);

        // The phone's tokens follow their own rules (MobileRefreshAsync), never the cookie's.
        if (presented is not { Client: RefreshTokenClient.Web })
        {
            throw SessionExpired();
        }

        if (presented.RevokedAt is not null)
        {
            if (presented.ReplacedById is not null && !RefreshTokenRules.WithinReuseGrace(presented.RevokedAt.Value, now))
            {
                await SignOutEverywhereAfterReuseAsync(presented, now, cancellationToken);
                permissions.Forget(presented.UserId);
            }

            throw SessionExpired();
        }

        if (presented.ExpiresAt <= now)
        {
            throw SessionExpired();
        }

        var user = await ActiveUserForRefreshAsync(presented.UserId, now, cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Claim the token atomically. If another request rotated it a moment earlier (two tabs at once), this one is
        // refused, within the reuse grace (RefreshTokenRules).
        if (!await refreshTokens.TryRevokeAsync(presented.Id, now, RevokeReason.Rotated, cancellationToken))
        {
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenReused, SessionExpiredMessage);
        }

        user.LastSeenAt = now;
        var session = await sessions.StartAsync(user, RefreshTokenClient.Web, presented.SessionId ?? Guid.NewGuid(), cancellationToken);
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
        if (token is { RevokedAt: null, Client: RefreshTokenClient.Web })
        {
            await refreshTokens.TryRevokeAsync(token.Id, clock.UtcNow, RevokeReason.SignedOut, cancellationToken);
        }
    }

    public Task<AuthSession> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
        => ChangePasswordAsync(userId, request, user => sessions.StartAsync(user, cancellationToken), cancellationToken);

    public async Task<AuthSession> MobileLoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await AuthenticateAsync(request, cancellationToken);
        var session = await sessions.StartAsync(user, RefreshTokenClient.Mobile, Guid.NewGuid(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<AuthSession> MobileRefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var presented = string.IsNullOrWhiteSpace(refreshToken)
            ? null
            : await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshToken), cancellationToken);
        if (presented is not { Client: RefreshTokenClient.Mobile, SessionId: { } sessionId })
        {
            throw SessionExpired();
        }

        var after = await refreshTokens.SuccessorsAsync(presented, cancellationToken);
        var grace = TimeSpan.FromSeconds(mobileOptions.Value.ReplayGraceSeconds);
        switch (MobileRefreshRules.Decide(StateOf(presented), after.Select(StateOf).ToList(), now, grace))
        {
            case MobileRefreshCase.Rotate:
                return await ContinueMobileSessionAsync(presented.UserId, sessionId, presented.Id, RevokeReason.Rotated, now, cancellationToken);

            case MobileRefreshCase.LostAnswerRetry:
                _security.LogInformation(
                    "Refresh token {TokenId} of user {UserId} was presented again within the grace; its session continues.",
                    presented.Id, presented.UserId);
                return await ContinueMobileSessionAsync(presented.UserId, sessionId, after[^1].Id, RevokeReason.Superseded, now, cancellationToken);

            case MobileRefreshCase.StaleReplay:
                _security.LogWarning(
                    "Refresh token {TokenId} of user {UserId} was presented again after the grace; ending that session.",
                    presented.Id, presented.UserId);
                await refreshTokens.RevokeSessionAsync(sessionId, now, RevokeReason.Revoked, cancellationToken);
                break;

            case MobileRefreshCase.Theft:
                _security.LogWarning(
                    "Refresh token {TokenId} of user {UserId} is held by two parties; ending every session.",
                    presented.Id, presented.UserId);
                await EndEverySessionAsync(presented.UserId, now, cancellationToken);
                permissions.Forget(presented.UserId);
                break;
        }

        throw SessionExpired();
    }

    public async Task MobileLogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var token = await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshToken), cancellationToken);
        if (token is { Client: RefreshTokenClient.Mobile, SessionId: { } sessionId })
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            await refreshTokens.RevokeSessionAsync(sessionId, clock.UtcNow, RevokeReason.SignedOut, cancellationToken);
            await devices.DeleteSessionAsync(sessionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public Task<AuthSession> MobileChangePasswordAsync(
        long userId,
        Guid? sessionId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
        => ChangePasswordAsync(
            userId,
            request,
            // the phone keeps its session id, so what is tied to it (its push device) stays
            user => sessions.StartAsync(user, RefreshTokenClient.Mobile, sessionId ?? Guid.NewGuid(), cancellationToken),
            cancellationToken);

    public async Task<CurrentUserResponse> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
        => await sessions.DescribeAsync(await GetActiveUserAsync(userId, cancellationToken), cancellationToken);

    /// <summary>Checks the email and password of an active account, rehashing the password and recording the sign-in.</summary>
    private async Task<User> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
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
        return user;
    }

    /// <summary>The token's owner, if they may still refresh. Otherwise every session of theirs ends.</summary>
    private async Task<User> ActiveUserForRefreshAsync(long userId, DateTime now, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(userId, now, cancellationToken);
            throw SessionExpired();
        }

        if (user.SessionsRevokedAt is not null)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
            throw new UnauthenticatedException(ErrorCodes.SignInSuspended, SessionRules.SuspendedMessage);
        }

        return user;
    }

    /// <summary>
    /// Claims <paramref name="claimId"/> atomically, so of two refreshes at once only one gets a new token, and issues
    /// the next token of the phone's session.
    /// </summary>
    private async Task<AuthSession> ContinueMobileSessionAsync(
        long userId,
        Guid sessionId,
        long claimId,
        RevokeReason reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var user = await ActiveUserForRefreshAsync(userId, now, cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (!await refreshTokens.TryRevokeAsync(claimId, now, reason, cancellationToken))
        {
            throw new UnauthenticatedException(ErrorCodes.RefreshTokenReused, SessionExpiredMessage);
        }

        user.LastSeenAt = now;
        var session = await sessions.StartAsync(user, RefreshTokenClient.Mobile, sessionId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await refreshTokens.SetReplacedByAsync(claimId, session.StoredRefreshToken.Id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return session;
    }

    private async Task<AuthSession> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        Func<User, Task<AuthSession>> startSession,
        CancellationToken cancellationToken)
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
        var session = await startSession(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return session;
    }

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

        await EndEverySessionAsync(token.UserId, now, cancellationToken);
    }

    private async Task EndEverySessionAsync(long userId, DateTime now, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeAllActiveForUserAsync(userId, now, cancellationToken);

        // whoever replayed the token may hold an access token from it: those stop working too
        if (await users.GetByIdAsync(userId, cancellationToken) is { } user)
        {
            user.SessionsValidFrom = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static MobileRefreshRules.Token StateOf(RefreshToken token) => new(token.ExpiresAt, token.RevokedAt, token.RevokeReason);

    private static UnauthenticatedException SessionExpired() => new(ErrorCodes.RefreshTokenInvalid, SessionExpiredMessage);
}
