using System.Security.Cryptography;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// The real API, hosted in this process against the load-test database, the way the integration tests host it. Only
/// what seeding needs differs: the simulated clock, mail kept in memory, no live connections, no limits on request
/// rates, and access tokens accepted whatever their dates (they are issued in the simulated past).
/// </summary>
public sealed class SeedingApi(string connectionString, SimulatedClock clock, SeedMailbox mailbox)
    : WebApplicationFactory<global::Program>
{
    /// <summary>This run's key for the setup endpoint, which creates the first super admin.</summary>
    public string SetupKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private readonly string _filesRoot = Path.Combine(Path.GetTempPath(), $"idi_seed_files_{Guid.NewGuid():N}");

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Runs every scheduled check once, as the hourly scheduler would at this moment of the simulation.</summary>
    public async Task RunScheduledChecksAsync()
    {
        foreach (var check in Enum.GetValues<ScheduledCheck>())
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(check);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Seeding");
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:SigningKey", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Setup:SuperAdminKey", SetupKey);
        builder.UseSetting("Frontend:BaseUrl", "https://loadtest.invalid");
        builder.UseSetting("Email:FromAddress", "no-reply@loadtest.invalid");
        builder.UseSetting("Company:TimeZone", SimulatedClock.TimeZone);
        builder.UseSetting("RateLimiting:UserPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "10000");
        builder.UseSetting("RateLimiting:SetupPermitsPerHour", "10000");
        builder.UseSetting("RateLimiting:ExportPermitsPerMinute", "10000");
        builder.UseSetting("RateLimiting:UploadPermitsPerMinute", "10000");
        // The seeder runs the scheduled checks itself, at simulated times.
        builder.UseSetting("NotificationScheduler:Enabled", "false");
        builder.UseSetting("FileStorage:RootPath", _filesRoot);
        builder.UseSetting("FileStorage:CleanupEnabled", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Error");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(mailbox);
            services.RemoveAll<ILivePublisher>();
            services.AddSingleton<ILivePublisher, SilentLivePublisher>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.ValidateLifetime = false);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_filesRoot))
        {
            Directory.Delete(_filesRoot, recursive: true);
        }
    }

    private sealed class SilentLivePublisher : ILivePublisher
    {
        public Task PublishAsync(IReadOnlyCollection<long> userIds, string signal, object? payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
