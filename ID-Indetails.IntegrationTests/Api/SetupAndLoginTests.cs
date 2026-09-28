using ID_Indetails.api.Constants;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SetupAndLoginTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Setup_endpoint_answers_404_without_the_right_key_even_for_an_invalid_body()
    {
        await (await _steps.CallSetupAsync(new { }, setupKey: null)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await _steps.CallSetupAsync(new { }, setupKey: "wrong-key")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Setup_creates_a_super_admin_and_sends_the_password_only_by_email()
    {
        var email = AuthTestSteps.NewEmail("setup");

        var response = await _steps.CallSetupAsync(
            new { email, firstName = "Rana", lastName = "Haddad", position = "Managing Director" }, ApiFactory.SetupKey);

        ((int)response.StatusCode).ShouldBe(201);
        var temporary = api.Emails.TemporaryPasswordFor(email);
        temporary.Length.ShouldBe(20);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(temporary);

        (await _steps.ScalarAsync<string>("SELECT role_code FROM users WHERE email = @e", ("@e", email))).ShouldBe(RoleCodes.SuperAdmin);
        (await _steps.ScalarAsync<int>("SELECT domain_code IS NULL FROM users WHERE email = @e", ("@e", email))).ShouldBe(1);
        (await _steps.ScalarAsync<int>("SELECT must_change_password FROM users WHERE email = @e", ("@e", email))).ShouldBe(1);
    }

    [Fact]
    public async Task Temporary_password_only_allows_changing_it()
    {
        var email = AuthTestSteps.NewEmail("temp");
        await _steps.CallSetupAsync(new { email, firstName = "Rana", lastName = "Haddad" }, ApiFactory.SetupKey);
        var temporary = api.Emails.TemporaryPasswordFor(email);

        var first = await (await Client.PostJsonAsync("/api/auth/login", new { email, password = temporary })).ShouldSignInAsync();
        first.User.MustChangePassword.ShouldBeTrue();
        new JsonWebToken(first.AccessToken).GetClaim(AuthClaimTypes.MustChangePassword).Value.ShouldBe("true");

        // Everything except "me" and "change password" is locked.
        (await Client.GetAsync("/api/auth/me", first.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/invitations", new { email = "x@test.local", role = "employee", domainCode = "design" }, first.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.PasswordChangeRequired);

        var changed = await (await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = temporary, newPassword = "my-own-long-passphrase" },
            first.AccessToken)).ShouldSignInAsync();

        changed.User.MustChangePassword.ShouldBeFalse();
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = temporary }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "my-own-long-passphrase" })).ShouldSignInAsync();
    }

    [Fact]
    public async Task Wrong_current_password_is_a_field_error()
    {
        var (_, _, admin) = await _steps.ActiveSuperAdminAsync();

        var error = await (await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "not-my-password", newPassword = "another-long-passphrase" },
            admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.CurrentPasswordIncorrect);

        error.Errors!.Keys.ShouldContain("currentPassword");
    }

    [Fact]
    public async Task Access_token_carries_the_agreed_claims_and_lasts_three_hours()
    {
        var (email, _, auth) = await _steps.ActiveSuperAdminAsync();
        var jwt = new JsonWebToken(auth.AccessToken);

        jwt.GetClaim(AuthClaimTypes.Email).Value.ShouldBe(email);
        jwt.GetClaim(AuthClaimTypes.FirstName).Value.ShouldBe("Rana");
        jwt.GetClaim(AuthClaimTypes.LastName).Value.ShouldBe("Haddad");
        jwt.GetClaim(AuthClaimTypes.Role).Value.ShouldBe(RoleCodes.SuperAdmin);
        jwt.TryGetClaim(AuthClaimTypes.Domain, out _).ShouldBeFalse();
        (jwt.ValidTo - jwt.IssuedAt).ShouldBe(TimeSpan.FromHours(3));
        auth.User.Permissions.ShouldContain(PermissionCodes.CompanyView);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_get_the_same_answer()
    {
        var (email, _, _) = await _steps.ActiveSuperAdminAsync();

        var unknown = await (await Client.PostJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "whatever-password" }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        var wrong = await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "whatever-password" }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);

        wrong.Message.ShouldBe(unknown.Message);
    }

    [Fact]
    public async Task Email_is_matched_case_insensitively()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PostJsonAsync("/api/auth/login", new { email = "  " + email.ToUpperInvariant(), password })).ShouldSignInAsync();
    }

    [Fact]
    public async Task Invalid_request_gets_the_uniform_validation_error()
    {
        var error = await (await Client.PostJsonAsync("/api/auth/login", new { email = "not-an-email" }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        error.Errors!.Keys.ShouldBe(["email", "password"], ignoreOrder: true);
    }

    [Theory]
    // Each row comes from a different error path; all must produce the same JSON shape.
    [InlineData("/api/auth/login", "{\"email\":\"bad\"}", "validation")]              // validation filter
    [InlineData("/api/auth/login", "{\"email\":\"n@test.local\",\"password\":\"x\"}", "authentication")] // service exception
    [InlineData("/api/invitations", "{}", "authentication")]                         // authorization handler
    [InlineData("/api/setup/super-admin", "{}", "not_found")]                        // setup key filter
    public async Task Every_error_path_returns_the_same_json_shape(string url, string json, string expectedType)
    {
        var response = await Client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        root.GetProperty("type").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);
        root.GetProperty("type").GetString().ShouldBe(expectedType);
        root.GetProperty("code").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Missing_or_forged_token_gets_the_uniform_401()
    {
        await (await Client.PostJsonAsync("/api/invitations", new { })).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.GetAsync("/api/auth/me", "not.a.jwt")).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Refresh_cookie_is_httponly_secure_strict_and_scoped_and_only_its_hash_is_stored()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();

        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password });
        var header = login.SetCookieHeaders().Single(h => h.StartsWith(ApiClientExtensions.RefreshCookieName + "="));
        var cookie = login.RefreshCookie()!;

        header.ToLowerInvariant().ShouldContain("httponly");
        header.ToLowerInvariant().ShouldContain("secure");
        header.ToLowerInvariant().ShouldContain("samesite=strict");
        header.ToLowerInvariant().ShouldContain("path=/api/auth");
        (await login.Content.ReadAsStringAsync()).ShouldNotContain(cookie);

        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM refresh_tokens WHERE token_hash = @v", ("@v", cookie))).ShouldBe(0);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM refresh_tokens WHERE token_hash = @h", ("@h", SecureTokens.Hash(cookie)))).ShouldBe(1);
    }
}
