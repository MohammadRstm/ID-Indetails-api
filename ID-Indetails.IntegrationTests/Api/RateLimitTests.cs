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
