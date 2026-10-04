using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Employees;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Changing a sign-in address (<c>employee:change_email</c>): someone else's, someone still invited, and one's own.</summary>
public class SignInEmailTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private Task<HttpResponseMessage> ChangeAsync(long userId, string email, string accessToken)
        => Client.PutJsonAsync($"/api/employees/{userId}/email", new { email }, accessToken);

    [Fact]
    public async Task A_new_address_signs_in_from_then_on_and_their_sessions_end()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (oldEmail, password, signedUp) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var id = signedUp.User.Id;
        var login = await Client.PostJsonAsync("/api/auth/login", new { email = oldEmail, password });
        var refresh = login.RefreshCookie();
        var held = (await login.ShouldSignInAsync()).AccessToken;
        var newEmail = AuthTestSteps.NewEmail("moved");

        var changed = await ChangeAsync(id, $"  {newEmail.ToUpperInvariant()} ", superAdmin.AccessToken);
        changed.IsSuccessStatusCode.ShouldBeTrue(await changed.Content.ReadAsStringAsync());
        (await changed.ReadAsync<EmployeeResponse>()).Email.ShouldBe(newEmail);

        // what they were signed in with stops at once
        await (await Client.GetAsync("/api/auth/me", held)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: refresh)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/auth/login", new { email = oldEmail, password })).ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var fresh = await (await Client.PostJsonAsync("/api/auth/login", new { email = newEmail, password })).ShouldSignInAsync();
        (await Client.GetAsync("/api/auth/me", fresh.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // both addresses are told; the old one isn't told the new one
        api.Emails.LastTo(newEmail).TextBody.ShouldContain("You now sign in");
        var toOld = api.Emails.LastTo(oldEmail);
        toOld.TextBody.ShouldContain("no longer signs in");
        toOld.TextBody.ShouldNotContain(newEmail);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'employee_email_changed' AND subject_id = @u AND payload IS NULL",
            ("@u", id))).ShouldBe(1);

        await (await ChangeAsync(id, newEmail, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.EmailUnchanged);
        var (takenEmail, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await (await ChangeAsync(id, takenEmail, superAdmin.AccessToken)).ShouldFailWithAsync(409, ErrorCodes.EmailAlreadyRegistered);
        await (await ChangeAsync(id, "not an address", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Someone_invited_gets_their_invitation_again_at_the_new_address()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var oldEmail = AuthTestSteps.NewEmail("typo");
        var invite = await Client.PostJsonAsync("/api/invitations", new { email = oldEmail, role = RoleCodes.Employee, domainCode = DomainCodes.Design }, superAdmin.AccessToken);
        ((int)invite.StatusCode).ShouldBe(201, await invite.Content.ReadAsStringAsync());
        var oldToken = api.Emails.InvitationTokenFor(oldEmail);
        var id = await _steps.ScalarAsync<long>("SELECT id FROM users WHERE email = @e", ("@e", oldEmail));
        var newEmail = AuthTestSteps.NewEmail("fixed");

        (await ChangeAsync(id, newEmail, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PostJsonAsync("/api/invitations/validate", new { token = oldToken }))
            .ShouldFailWithAsync(404, ErrorCodes.InvitationInvalid);
        api.Emails.CountTo(oldEmail).ShouldBe(1);
        await (await Client.PostJsonAsync("/api/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(newEmail),
            firstName = "Lina",
            lastName = "Saad",
            password = "linas-own-passphrase",
        })).ShouldSignInAsync();
    }

    [Fact]
    public async Task A_super_admin_changes_their_own_with_their_password_and_stays_signed_in_here_only()
    {
        var (oldEmail, password, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var elsewhere = (await Client.PostJsonAsync("/api/auth/login", new { email = oldEmail, password })).RefreshCookie();
        var newEmail = AuthTestSteps.NewEmail("super");

        await (await Client.PostJsonAsync("/api/auth/change-email", new { email = newEmail, currentPassword = "not-my-password" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.CurrentPasswordIncorrect);
        var response = await Client.PostJsonAsync("/api/auth/change-email", new { email = newEmail, currentPassword = password }, superAdmin.AccessToken);
        response.RefreshCookie().ShouldNotBeNull();
        var signedIn = await response.ShouldSignInAsync();
        signedIn.User.Email.ShouldBe(newEmail);

        (await Client.GetAsync("/api/auth/me", signedIn.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: elsewhere)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/auth/login", new { email = newEmail, password })).ShouldSignInAsync();
        api.Emails.LastTo(oldEmail).TextBody.ShouldContain("no longer signs in");

        // their own only this way, with the password
        await (await ChangeAsync(superAdmin.User.Id, AuthTestSteps.NewEmail(), signedIn.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
    }

    [Fact]
    public async Task Only_whoever_holds_employee_change_email_changes_an_address_and_never_a_deactivated_ones()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, adminPassword, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, password, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // no role but super admin holds it by default
        await (await ChangeAsync(employee.User.Id, AuthTestSteps.NewEmail(), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/auth/change-email", new { email = AuthTestSteps.NewEmail(), currentPassword = password }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // given it, an admin changes someone's, and their own with their password, but never a super admin's
        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions", new
        {
            changes = new[]
            {
                new { code = PermissionCodes.EmployeeChangeEmail, action = "grant", reason = "Looks after accounts" },
                new { code = PermissionCodes.MaterialCreate, action = "grant", reason = "Sees both domains" },
            },
        }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        (await ChangeAsync(employee.User.Id, AuthTestSteps.NewEmail(), admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await ChangeAsync(superAdmin.User.Id, AuthTestSteps.NewEmail(), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        await (await Client.PostJsonAsync("/api/auth/change-email", new { email = AuthTestSteps.NewEmail(), currentPassword = adminPassword }, admin.AccessToken))
            .ShouldSignInAsync();

        (await Client.PostEmptyAsync($"/api/employees/{employee.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await ChangeAsync(employee.User.Id, AuthTestSteps.NewEmail(), superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.UserDeactivated);
    }

    [Fact]
    public async Task Nothing_changes_when_the_new_address_cant_be_emailed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        api.Emails.FailNextSend = true;
        await (await ChangeAsync(employee.User.Id, AuthTestSteps.NewEmail(), superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);

        (await Client.GetAsync("/api/auth/me", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldSignInAsync();
    }
}
