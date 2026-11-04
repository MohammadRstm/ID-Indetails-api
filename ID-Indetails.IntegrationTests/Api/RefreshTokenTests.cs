using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class RefreshTokenTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<string> LoginCookieAsync(string email, string password)
        => (await Client.PostJsonAsync("/api/auth/login", new { email, password })).RefreshCookie()!;

    /// <summary>The client and lifetime in days of the person's newest valid refresh token, as "client:days".</summary>
    private Task<string> NewestSessionAsync(long userId)
        => _steps.ScalarAsync<string>(
            """
            SELECT CONCAT(client, ':', DATEDIFF(expires_at, created_at)) FROM refresh_tokens
            WHERE user_id = @u AND revoked_at IS NULL ORDER BY id DESC LIMIT 1
            """,
            ("@u", userId));

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
    public async Task The_installed_app_gets_a_30_day_session_that_a_browser_tab_never_shortens()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password, installedApp = true });
        var userId = (await login.ShouldSignInAsync()).User.Id;
        (await NewestSessionAsync(userId)).ShouldBe("installed:30");

        // a browser tab on the same Android phone shares the cookie and doesn't say it's the installed app
        var refreshed = await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: login.RefreshCookie());
        await refreshed.ShouldSignInAsync();
        (await NewestSessionAsync(userId)).ShouldBe("installed:30");
    }

    [Fact]
    public async Task A_website_session_becomes_the_installed_apps_when_the_installed_app_refreshes_it()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password });
        var userId = (await login.ShouldSignInAsync()).User.Id;
        (await NewestSessionAsync(userId)).ShouldBe("web:1");

        var refreshed = await Client.PostJsonAsync("/api/auth/refresh", new { installedApp = true }, refreshCookie: login.RefreshCookie());
        await refreshed.ShouldSignInAsync();
        (await NewestSessionAsync(userId)).ShouldBe("installed:30");
    }

    [Fact]
    public async Task Reusing_a_rotated_cookie_signs_the_user_out_everywhere()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var first = await LoginCookieAsync(email, password);
        var otherDevice = await LoginCookieAsync(email, password);

        var rotated = await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: first);
        var second = rotated.RefreshCookie()!;
        var userId = (await rotated.ShouldSignInAsync()).User.Id;

        // sent again at once, it's a lost answer: refused, and nobody else is signed out
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: first))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: otherDevice)).IsSuccessStatusCode.ShouldBeTrue();
        otherDevice = await LoginCookieAsync(email, password);
        await _steps.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = DATE_SUB(revoked_at, INTERVAL 1 MINUTE) WHERE user_id = @u AND replaced_by_id IS NOT NULL",
            ("@u", userId));

        // The old cookie comes back later (e.g. stolen): refused, and every session is revoked.
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
