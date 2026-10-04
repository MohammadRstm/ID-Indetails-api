using System.Net;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.HealthChecks;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace ID_Indetails.api.Extensions;

public static class HostingServiceCollectionExtensions
{
    public const string HealthPath = "/health";

    /// <summary>The health check anyone may call (an uptime monitor): it answers Healthy or Unhealthy, nothing more.</summary>
    public const string PublicHealthPath = "/api/health";

    public const long MaxRequestBodyBytes = 1024 * 1024;

    /// <summary>
    /// The health probe, trust in the proxies in front of the API when there are any (<see cref="ProxyOptions"/>), and,
    /// in production, a refusal to start on development settings.
    /// </summary>
    public static IServiceCollection AddHosting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // No request needs more than a little JSON; uploads set their own limit on their endpoint.
        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        var proxy = configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Host is left alone: links in emails come from Frontend:BaseUrl, never from the request.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = proxy.ForwardLimit;
            foreach (var network in proxy.KnownNetworks)
            {
                options.KnownIPNetworks.Add(IPNetwork.Parse(network));
            }
        });

        if (environment.IsProduction())
        {
            RequireProductionSettings(services);
        }

        return services;
    }

    /// <summary>Whether proxies are configured, so the forwarded headers are read at all.</summary>
    public static bool BehindProxy(IConfiguration configuration)
        => configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>()?.KnownNetworks.Length > 0;

    // Each of these would work locally and fail quietly in production: emailed links to localhost, mail to a mail
    // catcher, a browser on the developer's machine allowed to call the API, every visitor counted as the proxy.
    private static void RequireProductionSettings(IServiceCollection services)
    {
        services.AddOptions<FrontendOptions>()
            .Validate(o => IsPublicHttps(o.BaseUrl), "Frontend:BaseUrl must be the public https address of the client.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>()
            .Validate(o => !IsLocal(o.Host), "Email:Host must be the production SMTP server.")
            .Validate(o => o.UseStartTls, "Email:UseStartTls must be on, so mail and the SMTP password travel encrypted.")
            .Validate(o => !o.FromAddress.EndsWith(".local", StringComparison.OrdinalIgnoreCase), "Email:FromAddress must be a real address.")
            .ValidateOnStart();
        services.AddOptions<SetupOptions>().BindConfiguration(SetupOptions.SectionName)
            .Validate(o => o.SuperAdminKey.Length is 0 or >= 32, "Setup:SuperAdminKey must be empty or at least 32 characters.")
            .ValidateOnStart();
        services.AddOptions<ClientCorsOptions>().BindConfiguration(ClientCorsOptions.SectionName)
            .Validate(o => !o.AllowedOrigins.Any(origin => !IsPublicHttps(origin)), "Cors:AllowedOrigins may only list public https origins.")
            .ValidateOnStart();
        services.AddOptions<ProxyOptions>().BindConfiguration(ProxyOptions.SectionName)
            .Validate(o => o.KnownNetworks.Length > 0 && o.KnownNetworks.All(n => IPNetwork.TryParse(n, out _)),
                "Proxy:KnownNetworks must list the proxies' networks (CIDR), e.g. 172.16.0.0/12.")
            .ValidateOnStart();
    }

    private static bool IsPublicHttps(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !IsLocal(uri.Host);

    private static bool IsLocal(string host)
        => string.IsNullOrWhiteSpace(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
}
