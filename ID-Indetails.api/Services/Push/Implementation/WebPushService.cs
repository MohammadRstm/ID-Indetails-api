using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Push.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// The website's sessions are named by the refresh cookie, not the access token, so the browser's session is the one
/// the cookie's token belongs to: the caller's own, still live. One subscription is one device, as one Expo token is.
/// </summary>
public sealed class WebPushService(
    ICurrentUser currentUser,
    IUserDeviceRepository devices,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IOptions<PushOptions> options,
    IClock clock) : IWebPushService
{
    private bool On => options.Value is { Enabled: true, WebPush.Configured: true };

    public WebPushKeyResponse Key() => new(On ? options.Value.WebPush.PublicKey : null);

    public async Task RegisterAsync(string? refreshCookieValue, RegisterWebPushRequest request, CancellationToken cancellationToken = default)
    {
        if (!On)
        {
            throw new NotFoundException("Browsers get no pushes for now.");
        }

        var now = clock.UtcNow;
        var sessionId = await SessionOfAsync(refreshCookieValue, now, cancellationToken)
            ?? throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "Your session has ended. Please sign in again.");

        var subscription = new WebPushSubscription(request.Endpoint.Trim(), request.P256dh.Trim(), request.Auth.Trim());
        var token = WebPushEndpoints.PushTokenOf(subscription.Endpoint);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await devices.SaveAsync(currentUser.Id, sessionId, token, DevicePlatform.Web, null, subscription, now, cancellationToken);
        await devices.DeleteOtherTokensOfSessionAsync(sessionId, token, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(string? refreshCookieValue, CancellationToken cancellationToken = default)
    {
        if (await SessionOfAsync(refreshCookieValue, clock.UtcNow, cancellationToken) is { } sessionId)
        {
            await devices.DeleteSessionOfUserAsync(currentUser.Id, sessionId, cancellationToken);
        }
    }

    /// <summary>
    /// The session of the cookie's token, when it is the caller's website session and still live. The token itself may
    /// have just been rotated by a refresh running at the same time.
    /// </summary>
    private async Task<Guid?> SessionOfAsync(string? refreshCookieValue, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshCookieValue))
        {
            return null;
        }

        var token = await refreshTokens.GetByHashAsync(SecureTokens.Hash(refreshCookieValue), cancellationToken);
        return token is { Client: RefreshTokenClient.Web or RefreshTokenClient.Installed, SessionId: { } sessionId }
            && token.UserId == currentUser.Id
            && await refreshTokens.IsSessionLiveAsync(currentUser.Id, sessionId, now, cancellationToken)
                ? sessionId
                : null;
    }
}
