using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Implementation;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class AccessTokenServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc);

    private static AccessTokenService CreateService()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        return new AccessTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "test-issuer",
                Audience = "test-audience",
                SigningKey = new string('k', 64),
                AccessTokenMinutes = 180,
            }),
            clock.Object);
    }

    private static readonly User DesignAdmin = new()
    {
        Id = 42,
        Email = "sara@example.com",
        FirstName = "Sara",
        LastName = "Haddad",
        RoleCode = RoleCodes.Admin,
        DomainCode = DomainCodes.Design,
        Position = "Structural lead",
        Team = new Team { Id = 7, Name = "Structural", DomainCode = DomainCodes.Design },
    };

    [Fact]
    public void Token_carries_the_agreed_claims()
    {
        var (token, _) = CreateService().Create(DesignAdmin);
        var jwt = new JsonWebToken(token);

        jwt.GetClaim(AuthClaimTypes.UserId).Value.ShouldBe("42");
        jwt.GetClaim(AuthClaimTypes.Email).Value.ShouldBe("sara@example.com");
        jwt.GetClaim(AuthClaimTypes.FirstName).Value.ShouldBe("Sara");
        jwt.GetClaim(AuthClaimTypes.LastName).Value.ShouldBe("Haddad");
        jwt.GetClaim(AuthClaimTypes.Role).Value.ShouldBe("admin");
        jwt.GetClaim(AuthClaimTypes.Domain).Value.ShouldBe("design");
        jwt.GetClaim(AuthClaimTypes.Team).Value.ShouldBe("Structural");
        jwt.GetClaim(AuthClaimTypes.Position).Value.ShouldBe("Structural lead");
        jwt.GetClaim(AuthClaimTypes.MustChangePassword).Value.ShouldBe("false");
        jwt.Issuer.ShouldBe("test-issuer");
        jwt.Audiences.ShouldBe(["test-audience"]);
        jwt.Alg.ShouldBe("HS256");
    }

    [Fact]
    public void Token_expires_after_three_hours()
    {
        var (token, expiresAt) = CreateService().Create(DesignAdmin);

        expiresAt.ShouldBe(Now.AddHours(3));
        new JsonWebToken(token).ValidTo.ShouldBe(Now.AddHours(3));
    }

    [Fact]
    public void Super_admin_token_has_no_domain_and_absent_values_are_omitted()
    {
        var superAdmin = new User
        {
            Id = 1, Email = "rana@example.com", FirstName = "Rana", LastName = "H",
            RoleCode = RoleCodes.SuperAdmin, MustChangePassword = true,
        };

        var jwt = new JsonWebToken(CreateService().Create(superAdmin).Token);

        jwt.TryGetClaim(AuthClaimTypes.Domain, out _).ShouldBeFalse();
        jwt.TryGetClaim(AuthClaimTypes.Team, out _).ShouldBeFalse();
        jwt.TryGetClaim(AuthClaimTypes.Position, out _).ShouldBeFalse();
        jwt.GetClaim(AuthClaimTypes.MustChangePassword).Value.ShouldBe("true");
    }

    [Fact]
    public void Only_the_phone_app_s_tokens_carry_its_session_id()
    {
        var sessionId = Guid.NewGuid();

        new JsonWebToken(CreateService().Create(DesignAdmin, sessionId).Token)
            .GetClaim(AuthClaimTypes.SessionId).Value.ShouldBe(sessionId.ToString());
        new JsonWebToken(CreateService().Create(DesignAdmin).Token)
            .TryGetClaim(AuthClaimTypes.SessionId, out _).ShouldBeFalse();
    }
}
