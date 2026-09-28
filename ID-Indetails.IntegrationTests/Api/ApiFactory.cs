using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.IntegrationTests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Hosts the real API in memory against its own throwaway MySQL database, with email
/// captured instead of sent. Everything else (auth, validation, error handling, rate
/// limiting, repositories) is the production code.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SetupKey = "integration-test-setup-key";

    private readonly MigratedDatabaseFixture _database = new();

    public CapturingEmailSender Emails { get; } = new();

    public MigratedDatabaseFixture Database => _database;

    /// <summary>Per-IP auth requests per minute. High by default so tests don't trip it.</summary>
    protected virtual int AuthPermitsPerMinute => 10_000;

    public Task InitializeAsync() => _database.InitializeAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("Setup:SuperAdminKey", SetupKey);
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", AuthPermitsPerMinute.ToString());
        builder.UseSetting("RateLimiting:SetupPermitsPerHour", "10000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    /// <summary>An HTTPS client (the refresh cookie is Secure) that does not manage cookies itself.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}

/// <summary>Same API with a tight auth rate limit, to prove the limit is enforced.</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override int AuthPermitsPerMinute => 3;
}
