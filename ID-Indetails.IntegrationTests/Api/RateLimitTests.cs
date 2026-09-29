using ID_Indetails.api.Constants;
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
