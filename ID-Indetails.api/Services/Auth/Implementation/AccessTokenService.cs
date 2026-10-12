using System.Text;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ID_Indetails.api.Services.Auth.Implementation;

/// <summary>
/// Builds the access token: user id, email, first and last name, role, domain, team, position
/// and the must-change-password flag, signed with HMAC-SHA256. Tokens for the phone app also carry its session id.
/// </summary>
public sealed class AccessTokenService(IOptions<JwtOptions> options, IClock clock) : IAccessTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) Create(User user, Guid? mobileSessionId = null)
    {
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [AuthClaimTypes.UserId] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [AuthClaimTypes.Email] = user.Email,
            [AuthClaimTypes.FirstName] = user.FirstName ?? string.Empty,
            [AuthClaimTypes.LastName] = user.LastName ?? string.Empty,
            [AuthClaimTypes.Role] = user.RoleCode,
            [AuthClaimTypes.MustChangePassword] = user.MustChangePassword ? "true" : "false",
        };

        // Absent rather than empty when not set, so the client can test for presence.
        if (user.DomainCode is not null) claims[AuthClaimTypes.Domain] = user.DomainCode;
        if (user.Team is not null) claims[AuthClaimTypes.Team] = user.Team.Name;
        if (!string.IsNullOrEmpty(user.Position)) claims[AuthClaimTypes.Position] = user.Position;
        if (mobileSessionId is { } sessionId) claims[AuthClaimTypes.SessionId] = sessionId.ToString();

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });

        return (token, expiresAt);
    }
}
