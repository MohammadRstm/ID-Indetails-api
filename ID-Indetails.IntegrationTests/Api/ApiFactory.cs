using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
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

    public CapturingLivePublisher Live { get; } = new();

    /// <summary>Pushes are never sent to Expo from tests; they land here when <see cref="PushEnabled"/>.</summary>
    public CapturingPushProvider Push { get; } = new();

    /// <summary>Uploads of this API, deleted with it.</summary>
    public string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), $"idi_test_files_{Guid.NewGuid():N}");

    public MigratedDatabaseFixture Database => _database;

    /// <summary>Per-IP auth requests per minute. High by default so tests don't trip it.</summary>
    protected virtual int AuthPermitsPerMinute => 10_000;

    /// <summary>Per signed-in user requests per minute. High by default so tests don't trip it.</summary>
    protected virtual int UserPermitsPerMinute => 100_000;

    /// <summary>Off by default, so the other tests don't queue pushes.</summary>
    protected virtual bool PushEnabled => false;

    public Task InitializeAsync() => _database.InitializeAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(FilesRoot))
        {
            Directory.Delete(FilesRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("Setup:SuperAdminKey", SetupKey);
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", AuthPermitsPerMinute.ToString());
        builder.UseSetting("RateLimiting:SetupPermitsPerHour", "10000");
        builder.UseSetting("RateLimiting:UserPermitsPerMinute", UserPermitsPerMinute.ToString());
        builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", "100000");

        // Tests run the scheduled checks themselves, once their data is ready.
        builder.UseSetting("NotificationScheduler:Enabled", "false");
        builder.UseSetting("FileStorage:RootPath", FilesRoot);
        builder.UseSetting("FileStorage:CleanupEnabled", "false");
        builder.UseSetting("Push:DeviceCleanupEnabled", "false");
        builder.UseSetting("Push:Enabled", PushEnabled ? "true" : "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<ILivePublisher>();
            services.AddSingleton<ILivePublisher>(Live);
            services.RemoveAll<IPushProvider>();
            services.AddSingleton<IPushProvider>(Push);
            // the email sender was replaced above: wrap it again, as the API wraps the real one
            services.AddProtonSideEffectGuards();
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

/// <summary>Same API with pushes on, sent to <see cref="ApiFactory.Push"/>.</summary>
public sealed class PushApiFactory : ApiFactory
{
    protected override bool PushEnabled => true;
}

/// <summary>Same API with a tight per-user limit, to prove every signed-in endpoint is limited.</summary>
public sealed class UserRateLimitedApiFactory : ApiFactory
{
    protected override int UserPermitsPerMinute => 5;
}
