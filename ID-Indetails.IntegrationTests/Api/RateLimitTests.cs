using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class RateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_attempts_beyond_the_limit_are_refused()
    {
        var client = api.CreateApiClient();

        for (var i = 0; i < 3; i++)
        {
            await (await client.PostJsonAsync("/api/auth/login", new { email = "guess@test.local", password = $"guess-number-{i}" }))
                .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        }

        await (await client.PostJsonAsync("/api/auth/login", new { email = "guess@test.local", password = "guess-number-4" }))
            .ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
    }
}

/// <summary>Its own factory, so the auth limit starts fresh.</summary>
public class PasswordResetRateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Reset_requests_share_the_auth_limit()
    {
        var client = api.CreateApiClient();

        for (var i = 0; i < 3; i++)
        {
            ((int)(await client.PostJsonAsync("/api/auth/forgot-password", new { email = "guess@test.local" })).StatusCode).ShouldBe(204);
        }

        await (await client.PostJsonAsync("/api/auth/forgot-password", new { email = "guess@test.local" }))
            .ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        await (await client.PostJsonAsync("/api/auth/reset-password", new { token = "guessed-token", password = "a-much-longer-passphrase" }))
            .ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
    }
}

public class UserRateLimitTests(UserRateLimitedApiFactory api) : IClassFixture<UserRateLimitedApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Each_signed_in_user_has_their_own_request_limit()
    {
        var (_, _, first) = await _steps.ActiveSuperAdminAsync();
        var (_, _, second) = await _steps.ActiveSuperAdminAsync();

        // Signing up used one request each (the password change); the limit is 5 a minute.
        for (var i = 0; i < 4; i++)
        {
            (await _steps.Client.GetAsync("/api/teams", first.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        await (await _steps.Client.GetAsync("/api/teams", first.AccessToken)).ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        (await _steps.Client.GetAsync("/api/teams", second.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }
}

public class PortalRateLimitTests(UserRateLimitedApiFactory api) : IClassFixture<UserRateLimitedApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    /// <summary>A client invited to a Design project who accepted: signed in to the portal.</summary>
    private async Task<string> ClientTokenAsync(AuthResponse superAdmin)
    {
        var email = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem", label = "Client", domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = email, label = "Office" } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        var project = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Project " + Guid.NewGuid().ToString("N")[..8], domainCode = DomainCodes.Design, clientId = client.Id }, superAdmin.AccessToken))
            .ReadAsync<IdOnly>();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/portal/invitations", new { email }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept",
            new { token = api.Emails.InvitationTokenFor(email), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase" });
        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());
        return (await accepted.ReadAsync<PortalAuthResponse>()).AccessToken;
    }

    [Fact]
    public async Task A_signed_in_client_has_their_own_request_limit_not_their_offices_address()
    {
        // two super admins, so neither uses up their own limit of 5 setting a client up
        var (_, _, first) = await _steps.ActiveSuperAdminAsync();
        var (_, _, second) = await _steps.ActiveSuperAdminAsync();
        var rita = await ClientTokenAsync(first);
        var sami = await ClientTokenAsync(second);

        for (var i = 0; i < 5; i++)
        {
            (await Client.GetAsync("/api/portal/me", rita)).IsSuccessStatusCode.ShouldBeTrue();
        }

        await (await Client.GetAsync("/api/portal/me", rita)).ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        // another client from the same address is unaffected
        (await Client.GetAsync("/api/portal/me", sami)).IsSuccessStatusCode.ShouldBeTrue();
    }
}

/// <summary>Its own factory: refreshing must keep working for people whose shared address used up the sign-in limit.</summary>
public class RefreshRateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Refreshing_has_its_own_limit_so_failed_sign_ins_from_the_same_address_dont_sign_everyone_out()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var cookie = (await _steps.Client.PostJsonAsync("/api/auth/login", new { email, password })).RefreshCookie()!;

        var refused = false;
        for (var i = 0; i < 10 && !refused; i++)
        {
            refused = (int)(await _steps.Client.PostJsonAsync("/api/auth/login", new { email = "guess@test.local", password = $"guess-{i}" })).StatusCode == 429;
        }

        refused.ShouldBeTrue();
        await (await _steps.Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie)).ShouldSignInAsync();
    }
}

public class CredentialRateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Guessing_the_current_password_with_a_stolen_token_is_limited_per_person()
    {
        var (_, _, auth) = await _steps.ActiveSuperAdminAsync();

        // signing up changed the temporary password once; the limit is 3 a minute
        for (var i = 0; i < 2; i++)
        {
            await (await _steps.Client.PostJsonAsync("/api/auth/change-password",
                new { currentPassword = $"guess-number-{i}", newPassword = "a-brand-new-passphrase" }, auth.AccessToken))
                .ShouldFailWithAsync(400, ErrorCodes.CurrentPasswordIncorrect);
        }

        var limited = await _steps.Client.PostJsonAsync("/api/auth/change-password",
            new { currentPassword = "guess-number-9", newPassword = "a-brand-new-passphrase" }, auth.AccessToken);
        await limited.ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }
}
