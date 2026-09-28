using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class RefreshTokenTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<string> LoginCookieAsync(string email, string password)
        => (await Client.PostJsonAsync("/api/auth/login", new { email, password })).RefreshCookie()!;

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_returns_a_new_access_token()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var first = await LoginCookieAsync(email, password);

        var response = await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: first);
        var auth = await response.ShouldSignInAsync();
        var second = response.RefreshCookie();

        second.ShouldNotBeNull();
        second.ShouldNotBe(first);
        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        (await Client.GetAsync("/api/auth/me", auth.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Reusing_a_rotated_cookie_signs_the_user_out_everywhere()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var first = await LoginCookieAsync(email, password);
        var otherDevice = await LoginCookieAsync(email, password);

        var second = (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: first)).RefreshCookie()!;

        // The old cookie comes back (e.g. stolen): refused, and every session is revoked.
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: first))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: second))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: otherDevice))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Refresh_without_or_with_an_unknown_cookie_fails_and_clears_the_cookie()
    {
        await (await Client.PostEmptyAsync("/api/auth/refresh")).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);

        var response = await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: "made-up-value");
        await response.ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        response.SetCookieHeaders().ShouldContain(h => h.StartsWith(ApiClientExtensions.RefreshCookieName + "=;"));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_clears_the_cookie()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var cookie = await LoginCookieAsync(email, password);

        var logout = await Client.PostEmptyAsync("/api/auth/logout", refreshCookie: cookie);
        ((int)logout.StatusCode).ShouldBe(204);
        logout.SetCookieHeaders().ShouldContain(h => h.StartsWith(ApiClientExtensions.RefreshCookieName + "=;"));

        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions()
    {
        var (email, password, auth) = await _steps.ActiveSuperAdminAsync();
        var otherDevice = await LoginCookieAsync(email, password);

        await (await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = password, newPassword = "a-brand-new-passphrase" },
            auth.AccessToken)).ShouldSignInAsync();

        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: otherDevice))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }
}
