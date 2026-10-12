using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Push.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// A device belongs to the phone app's session (the access token's <c>sid</c>), so a session that ends takes its pushes
/// with it. One push token is one device: registering it again, by anyone, moves it, so a shared phone never gets the
/// previous person's pushes. A session keeps one device: a new token replaces its earlier one.
/// </summary>
public sealed class DeviceService(
    ICurrentUser currentUser,
    IUserDeviceRepository devices,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock) : IDeviceService
{
    public async Task<DeviceResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = currentUser.SessionId
            ?? throw new ForbiddenException(ErrorCodes.Forbidden, "Only the phone app registers for pushes.");
        var now = clock.UtcNow;
        if (!await refreshTokens.IsSessionLiveAsync(currentUser.Id, sessionId, now, cancellationToken))
        {
            throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "Your session has ended. Please sign in again.");
        }

        var token = request.PushToken.Trim();
        var appVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim();
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await devices.SaveAsync(currentUser.Id, sessionId, token, request.Platform!.Value, appVersion, now, cancellationToken);
            await devices.DeleteOtherTokensOfSessionAsync(sessionId, token, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var device = await devices.GetByTokenAsync(token, cancellationToken)
            ?? throw new InvalidOperationException("The device just registered wasn't found.");
        return ToResponse(device, sessionId);
    }

    public async Task DeleteCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.SessionId is { } sessionId)
        {
            await devices.DeleteSessionOfUserAsync(currentUser.Id, sessionId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken = default)
        => (await devices.ListLiveForUserAsync(currentUser.Id, clock.UtcNow, cancellationToken))
            .Select(d => ToResponse(d, currentUser.SessionId))
            .ToList();

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var device = await devices.GetForUserAsync(id, currentUser.Id, cancellationToken)
            ?? throw new NotFoundException("Device not found.");

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await refreshTokens.RevokeSessionAsync(device.SessionId, clock.UtcNow, RevokeReason.SignedOut, cancellationToken);
        await devices.DeleteSessionAsync(device.SessionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<int> DeleteDeadAsync(CancellationToken cancellationToken = default)
        => devices.DeleteDeadAsync(clock.UtcNow, cancellationToken);

    private static DeviceResponse ToResponse(UserDevice d, Guid? callerSession)
        => new(d.Id, d.Platform, d.AppVersion, d.CreatedAt, d.UpdatedAt, d.SessionId == callerSession);
}
