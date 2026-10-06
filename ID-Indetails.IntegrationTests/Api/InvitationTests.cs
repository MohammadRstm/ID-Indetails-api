using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Invitations;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class InvitationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Invited_person_signs_up_through_the_emailed_link_and_is_signed_in()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail("designer");

        var created = await Client.PostJsonAsync("/api/invitations", new
        {
            email,
            role = "admin",
            domainCode = "design",
            position = "Design Director",
            pay = new { basis = "monthly", amount = 3000, currency = "usd" },
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201);
        (await created.ReadAsync<InvitationResponse>()).Domain.ShouldBe("design");

        // The email names the inviter and carries a one-time link.
        var message = api.Emails.LastTo(email);
        message.TextBody.ShouldContain("Rana Haddad");
        var token = api.Emails.InvitationTokenFor(email);

        var preview = await (await Client.PostJsonAsync("/api/invitations/validate", new { token }))
            .ReadAsync<InvitationPreviewResponse>();
        preview.Email.ShouldBe(email);
        preview.Role.ShouldBe("admin");
        preview.Position.ShouldBe("Design Director");

        var accepted = await Client.PostJsonAsync("/api/invitations/accept", new
        {
            token, firstName = "Sara", lastName = "Khoury", phone = "+961 70 123 456", password = "saras-own-passphrase",
        });
        var auth = await accepted.ShouldSignInAsync();
        accepted.RefreshCookie().ShouldNotBeNull();

        var jwt = new JsonWebToken(auth.AccessToken);
        jwt.GetClaim(AuthClaimTypes.FirstName).Value.ShouldBe("Sara");
        jwt.GetClaim(AuthClaimTypes.Role).Value.ShouldBe("admin");
        jwt.GetClaim(AuthClaimTypes.Domain).Value.ShouldBe("design");
        jwt.GetClaim(AuthClaimTypes.Position).Value.ShouldBe("Design Director");

        (await _steps.ScalarAsync<string>("SELECT status FROM users WHERE email = @e", ("@e", email))).ShouldBe("active");
        // a salary has no fixed hourly rate: it is worked out month by month
        (await _steps.ScalarAsync<decimal>(
            "SELECT p.amount FROM user_pay_history p JOIN users u ON u.id = p.user_id WHERE u.email = @e AND p.basis = 'monthly'",
            ("@e", email))).ShouldBe(3000m);

        // The link works once.
        await (await Client.PostJsonAsync("/api/invitations/validate", new { token }))
            .ShouldFailWithAsync(404, ErrorCodes.InvitationInvalid);
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "saras-own-passphrase" })).ShouldSignInAsync();
    }

    [Fact]
    public async Task Invited_person_cannot_sign_in_before_accepting()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail("pending");
        await Client.PostJsonAsync("/api/invitations", new { email, role = "employee", domainCode = "execution" }, superAdmin.AccessToken);

        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "anything-at-all" }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Admin_can_only_invite_admins_and_employees_into_their_own_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, "admin", "design");

        await (await Client.PostJsonAsync("/api/invitations",
                new { email = AuthTestSteps.NewEmail(), role = "employee", domainCode = "execution" }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);

        await (await Client.PostJsonAsync("/api/invitations",
                new { email = AuthTestSteps.NewEmail(), role = "super_admin", pay = new { basis = "monthly", amount = 2000, currency = "usd" } },
                designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);

        // No domain given: the admin's own domain is used.
        var created = await Client.PostJsonAsync("/api/invitations",
            new { email = AuthTestSteps.NewEmail(), role = "employee" }, designAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201);
        (await created.ReadAsync<InvitationResponse>()).Domain.ShouldBe("design");
    }

    [Fact]
    public async Task Employees_cannot_invite_anyone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, "employee", "execution");

        employee.User.Permissions.ShouldNotContain(PermissionCodes.EmployeeInvite);
        employee.User.Permissions.ShouldContain(PermissionCodes.MaterialCreate);

        await (await Client.PostJsonAsync("/api/invitations",
                new { email = AuthTestSteps.NewEmail(), role = "employee", domainCode = "execution" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_resend_to_someone_in_the_other_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, "admin", "design");

        var invited = await (await Client.PostJsonAsync("/api/invitations",
                new { email = AuthTestSteps.NewEmail(), role = "employee", domainCode = "execution" }, superAdmin.AccessToken))
            .ReadAsync<InvitationResponse>();

        await (await Client.PostEmptyAsync($"/api/invitations/{invited.UserId}/resend", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Same_email_cannot_be_invited_twice()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail();
        await Client.PostJsonAsync("/api/invitations", new { email, role = "employee", domainCode = "design" }, superAdmin.AccessToken);

        await (await Client.PostJsonAsync("/api/invitations",
                new { email = email.ToUpperInvariant(), role = "employee", domainCode = "design" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.EmailAlreadyRegistered);
    }

    [Fact]
    public async Task Resending_replaces_the_link()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail();
        var invited = await (await Client.PostJsonAsync("/api/invitations",
            new { email, role = "employee", domainCode = "design" }, superAdmin.AccessToken)).ReadAsync<InvitationResponse>();
        var oldToken = api.Emails.InvitationTokenFor(email);

        (await Client.PostEmptyAsync($"/api/invitations/{invited.UserId}/resend", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var newToken = api.Emails.InvitationTokenFor(email);

        newToken.ShouldNotBe(oldToken);
        await (await Client.PostJsonAsync("/api/invitations/validate", new { token = oldToken }))
            .ShouldFailWithAsync(404, ErrorCodes.InvitationInvalid);
        (await Client.PostJsonAsync("/api/invitations/validate", new { token = newToken })).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Invitation_is_not_saved_when_the_email_cannot_be_sent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail();

        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync("/api/invitations", new { email, role = "employee", domainCode = "design" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);

        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM users WHERE email = @e", ("@e", email))).ShouldBe(0);
        ((int)(await Client.PostJsonAsync("/api/invitations", new { email, role = "employee", domainCode = "design" }, superAdmin.AccessToken)).StatusCode)
            .ShouldBe(201);
    }

    [Fact]
    public async Task Team_must_belong_to_the_invitation_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var executionTeam = await _steps.ScalarAsync<long>(
            "INSERT INTO teams (domain_code, name, created_at) VALUES ('execution', @n, UTC_TIMESTAMP(3)); SELECT LAST_INSERT_ID();",
            ("@n", "Site " + Guid.NewGuid().ToString("N")[..6]));

        var error = await (await Client.PostJsonAsync("/api/invitations",
                new { email = AuthTestSteps.NewEmail(), role = "employee", domainCode = "design", teamId = executionTeam }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.TeamNotInDomain);
        error.Errors!.Keys.ShouldContain("teamId");
    }

    [Fact]
    public async Task Unknown_invitation_token_is_refused()
    {
        await (await Client.PostJsonAsync("/api/invitations/accept", new
        {
            token = "no-such-token", firstName = "A", lastName = "B", password = "long-enough-password",
        })).ShouldFailWithAsync(404, ErrorCodes.InvitationInvalid);
    }
}
