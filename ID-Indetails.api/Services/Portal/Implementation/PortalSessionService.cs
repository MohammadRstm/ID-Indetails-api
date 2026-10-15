using System.Text;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// Portal sessions: a rotating refresh token in the portal cookie and a short access token for the portal's audience. The
/// token carries the account id as <see cref="AuthClaimTypes.PortalAccountId"/> and no staff claims, so no staff endpoint
/// can read a user out of it.
/// </summary>
public sealed class PortalSessionService(
    IPortalSessionRepository sessions,
    IPortalAccessRepository access,
    IOptions<JwtOptions> jwtOptions,
    IOptions<PortalOptions> portalOptions,
    IClock clock) : IPortalSessionService
{
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<PortalAuthSession> StartAsync(PortalAccount account, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var (cookieValue, storedHash) = SecureTokens.NewRefreshToken();
        var refreshExpiresAt = now.AddDays(portalOptions.Value.RefreshTokenDays);

        var stored = new PortalRefreshToken
        {
            AccountId = account.Id,
            TokenHash = storedHash,
            SessionId = sessionId,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt,
        };
        sessions.Add(stored);

        var (accessToken, accessExpiresAt) = CreateAccessToken(account, now);
        var response = new PortalAuthResponse
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = accessExpiresAt,
            Account = await DescribeAsync(account, cancellationToken),
        };

        return new PortalAuthSession(response, cookieValue, refreshExpiresAt, stored);
    }

    public async Task<PortalAccountResponse> DescribeAsync(PortalAccount account, CancellationToken cancellationToken = default)
    {
        var projects = await access.ProjectsOfAsync(account.Id, cancellationToken);
        return new PortalAccountResponse
        {
            Id = account.Id,
            Email = account.Email,
            FirstName = account.FirstName ?? string.Empty,
            LastName = account.LastName ?? string.Empty,
            Projects = projects.Select(p => new PortalProjectResponse(p.Id, p.Title, p.Status)).ToList(),
        };
    }

    private (string Token, DateTime ExpiresAt) CreateAccessToken(PortalAccount account, DateTime now)
    {
        var jwt = jwtOptions.Value;
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = portalOptions.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.PortalAccountId] = account.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                [AuthClaimTypes.Email] = account.Email,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });

        return (token, expiresAt);
    }
}
