using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class PasswordResetTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "a-brand-new-passphrase";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task A_reset_link_sets_a_new_password_once_and_signs_out_everywhere()
    {
        var (email, password, _) = await EmployeeAsync();
        var session = (await Client.PostJsonAsync("/api/auth/login", new { email, password })).RefreshCookie();

        await ForgotAsync(email);
        var message = api.Emails.LastTo(email);
        message.Subject.ShouldBe("Reset your ID InDetails password");
        message.TextBody.ShouldContain("/reset-password?token=");
        var token = api.Emails.ResetTokenFor(email);

        (await Client.PostJsonAsync("/api/auth/reset-password", new { token, password = NewPassword })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = NewPassword })).ShouldSignInAsync();
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: session)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token, password = "yet-another-passphrase" }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);
    }

    [Fact]
    public async Task Asking_never_reveals_whether_an_email_belongs_to_anyone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (deactivatedEmail, _, deactivated) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/employees/{deactivated.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var invitedEmail = AuthTestSteps.NewEmail("invited");
        (await Client.PostJsonAsync("/api/invitations", new { email = invitedEmail, role = RoleCodes.Employee, domainCode = DomainCodes.Design }, superAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        foreach (var address in new[] { deactivatedEmail, invitedEmail, AuthTestSteps.NewEmail("nobody") })
        {
            var sent = api.Emails.CountTo(address);
            await ForgotAsync(address);
            api.Emails.CountTo(address).ShouldBe(sent);
        }

        // An email that can't be sent gets the same answer too.
        var (email, _, _) = await EmployeeAsync();
        var invitations = api.Emails.CountTo(email);
        api.Emails.FailNextSend = true;
        await ForgotAsync(email);
        api.Emails.CountTo(email).ShouldBe(invitations);
    }

    [Fact]
    public async Task Only_the_latest_link_works_and_only_until_it_expires()
    {
        var (email, _, employee) = await EmployeeAsync();

        await ForgotAsync(email);
        var first = api.Emails.ResetTokenFor(email);
        await ForgotAsync(email.ToUpperInvariant());
        var second = api.Emails.ResetTokenFor(email);

        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token = first, password = NewPassword }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);

        await _steps.ScalarAsync<int>(
            "UPDATE password_reset_tokens SET expires_at = UTC_TIMESTAMP(3) - INTERVAL 1 MINUTE WHERE user_id = @id; SELECT 1",
            ("@id", employee.User.Id));
        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token = second, password = NewPassword }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);
    }

    [Fact]
    public async Task A_link_stops_working_once_the_account_is_deactivated()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ForgotAsync(email);

        (await Client.PostEmptyAsync($"/api/employees/{employee.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token = api.Emails.ResetTokenFor(email), password = NewPassword }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);
    }

    [Fact]
    public async Task A_temporary_password_can_be_reset_too()
    {
        var email = AuthTestSteps.NewEmail("super");
        (await _steps.CallSetupAsync(new { email, firstName = "Rana", lastName = "Haddad" }, ApiFactory.SetupKey)).IsSuccessStatusCode.ShouldBeTrue();

        await ForgotAsync(email);
        (await Client.PostJsonAsync("/api/auth/reset-password", new { token = api.Emails.ResetTokenFor(email), password = NewPassword }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await Client.PostJsonAsync("/api/auth/login", new { email, password = NewPassword })).ShouldSignInAsync())
            .User.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Requests_are_validated()
    {
        await (await Client.PostJsonAsync("/api/auth/forgot-password", new { email = "not-an-email" })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token = "some-token", password = "short" }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/auth/reset-password", new { token = "made-up-token", password = NewPassword }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);
    }

    private async Task<(string Email, string Password, AuthResponse Auth)> EmployeeAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        return await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
    }

    /// <summary>Asks for a reset link; the answer is always an empty 204.</summary>
    private async Task ForgotAsync(string email)
    {
        var response = await Client.PostJsonAsync("/api/auth/forgot-password", new { email });
        ((int)response.StatusCode).ShouldBe(204, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }
}
