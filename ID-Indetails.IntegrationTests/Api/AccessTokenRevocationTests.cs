using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Signing out everywhere ends the access tokens already handed out, not only the refresh tokens.</summary>
public class AccessTokenRevocationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<(AuthResponse SuperAdmin, string Email, string Password, AuthResponse Auth, string Cookie)> SignedInEmployeeAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password });
        var cookie = login.RefreshCookie()!;
        return (superAdmin, email, password, await login.ShouldSignInAsync(), cookie);
    }

    private async Task ShouldBeRefusedAsync(string accessToken)
        => await (await Client.GetAsync("/api/auth/me", accessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);

    [Fact]
    public async Task A_password_reset_ends_the_access_tokens_already_handed_out()
    {
        var (_, email, _, auth, _) = await SignedInEmployeeAsync();
        (await Client.GetAsync("/api/auth/me", auth.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        ((int)(await Client.PostJsonAsync("/api/auth/forgot-password", new { email })).StatusCode).ShouldBe(204);
        var reset = await Client.PostJsonAsync("/api/auth/reset-password", new { token = api.Emails.ResetTokenFor(email), password = "a-whole-new-passphrase" });
        ((int)reset.StatusCode).ShouldBe(204, await reset.Content.ReadAsStringAsync());

        await ShouldBeRefusedAsync(auth.AccessToken);
    }

    [Fact]
    public async Task Changing_your_password_keeps_this_session_and_ends_the_others_access_tokens()
    {
        var (_, _, password, otherDevice, _) = await SignedInEmployeeAsync();
        var thisDevice = await (await Client.PostJsonAsync("/api/auth/login", new { email = otherDevice.User.Email, password })).ShouldSignInAsync();
        // a token's time is whole seconds: the other device signed in a second earlier
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var changed = await (await Client.PostJsonAsync("/api/auth/change-password",
            new { currentPassword = password, newPassword = "a-brand-new-passphrase" }, thisDevice.AccessToken)).ShouldSignInAsync();

        (await Client.GetAsync("/api/auth/me", changed.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await ShouldBeRefusedAsync(otherDevice.AccessToken);
    }

    [Fact]
    public async Task Deactivating_someone_ends_their_access_token_on_every_endpoint()
    {
        var (superAdmin, _, _, auth, _) = await SignedInEmployeeAsync();

        (await Client.PostEmptyAsync($"/api/employees/{auth.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // endpoints that only ask for a signed-in person included
        await (await Client.GetAsync("/api/notifications", auth.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostJsonAsync("/api/bug-reports", new { title = "Still here", description = "After leaving" }, auth.AccessToken))
            .ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task A_replayed_refresh_token_ends_the_access_tokens_too()
    {
        var (_, _, _, _, cookie) = await SignedInEmployeeAsync();
        var rotated = await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie)).ShouldSignInAsync();

        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);

        await ShouldBeRefusedAsync(rotated.AccessToken);
    }
}
