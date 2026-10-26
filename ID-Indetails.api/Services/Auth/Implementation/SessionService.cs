using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Auth.Implementation;

public sealed class SessionService(
    IAccessTokenService accessTokens,
    IRefreshTokenRepository refreshTokens,
    IPermissionService permissions,
    IOptions<RefreshTokenOptions> refreshOptions,
    IOptions<MobileSessionOptions> mobileOptions,
    IClock clock) : ISessionService
{
    public Task<AuthSession> StartAsync(User user, CancellationToken cancellationToken = default)
        => StartAsync(user, RefreshTokenClient.Web, Guid.NewGuid(), cancellationToken);

    public async Task<AuthSession> StartAsync(User user, RefreshTokenClient client, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var mobile = client == RefreshTokenClient.Mobile;
        var (cookieValue, storedHash) = SecureTokens.NewRefreshToken();
        var refreshExpiresAt = now.AddDays(mobile ? mobileOptions.Value.RefreshTokenDays : refreshOptions.Value.ExpirationDays);

        var stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = storedHash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt,
            Client = client,
            SessionId = sessionId,
        };
        refreshTokens.Add(stored);

        var (accessToken, accessExpiresAt) = accessTokens.Create(user, mobile ? sessionId : null);

        var response = new AuthResponse
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = accessExpiresAt,
            User = await DescribeAsync(user, cancellationToken),
        };

        return new AuthSession(response, cookieValue, refreshExpiresAt, stored);
    }

    public async Task<CurrentUserResponse> DescribeAsync(User user, CancellationToken cancellationToken = default)
    {
        var access = await permissions.ResolveAsync(user, cancellationToken);

        return new CurrentUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Role = user.RoleCode,
            Domain = user.DomainCode,
            Team = user.Team is null ? null : new TeamSummaryResponse(user.Team.Id, user.Team.Name),
            Position = user.Position,
            Phone = user.Phone,
            JoinedOn = user.JoinedOn,
            PhotoFileId = user.PhotoFileId,
            MustChangePassword = user.MustChangePassword,
            Permissions = access.Permissions,
            IsJoker = access.IsJoker,
            ProtonReplySuggestions = user.ProtonReplySuggestions,
            VisibleDomains = access.VisibleDomains,
        };
    }
}
