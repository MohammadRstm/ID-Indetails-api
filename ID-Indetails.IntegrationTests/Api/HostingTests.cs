using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>What the container platform relies on: the health probe, and production refusing development settings.</summary>
public class HostingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_health_probe_answers_without_signing_in_and_says_nothing_more()
    {
        var response = await api.CreateApiClient().GetAsync("/health");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }
}

public class ProductionSettingsTests
{
    [Fact]
    public async Task Production_starts_with_production_settings()
    {
        var api = new ProductionApiFactory(ProductionApiFactory.Valid);
        await api.InitializeAsync();
        try
        {
            (await api.CreateApiClient().GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await api.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Frontend:BaseUrl", "http://localhost:5173")]
    [InlineData("Frontend:BaseUrl", "http://app.example.com")]
    [InlineData("Email:Host", "127.0.0.1")]
    [InlineData("Email:FromAddress", "no-reply@id-indetails.local")]
    [InlineData("Cors:AllowedOrigins:0", "http://localhost:5173")]
    [InlineData("Proxy:KnownNetworks:0", null)]
    [InlineData("Email:UseStartTls", "false")]
    [InlineData("Setup:SuperAdminKey", "short-setup-key")]
    [InlineData("ContentStore:Provider", "Local")]
    public async Task Production_refuses_to_start_on_development_settings(string key, string? value)
    {
        // null: the setting is missing
        var settings = new Dictionary<string, string>(ProductionApiFactory.Valid);
        if (value is null) settings.Remove(key);
        else settings[key] = value;
        var api = new ProductionApiFactory(settings);
        await api.InitializeAsync();
        try
        {
            Should.Throw<OptionsValidationException>(() => api.CreateApiClient());
        }
        finally
        {
            await api.DisposeAsync();
        }
    }
}

/// <summary>The API in the Production environment, with the given settings on top of the test ones.</summary>
public sealed class ProductionApiFactory(IReadOnlyDictionary<string, string> settings) : ApiFactory
{
    public static readonly IReadOnlyDictionary<string, string> Valid = new Dictionary<string, string>
    {
        ["Frontend:BaseUrl"] = "https://app.example.com",
        ["Email:Host"] = "smtp.example.com",
        ["Email:FromAddress"] = "no-reply@example.com",
        ["Email:UseStartTls"] = "true",
        ["Setup:SuperAdminKey"] = "a-production-setup-key-of-at-least-32-characters",
        ["Proxy:KnownNetworks:0"] = "172.16.0.0/12",
        // the client portal's files are in R2 in production (these are never used: the portal's jobs are off in tests)
        ["ContentStore:Provider"] = "R2",
        ["ContentStore:R2:AccountId"] = "0123456789abcdef0123456789abcdef",
        ["ContentStore:R2:Bucket"] = "portal-test",
        ["ContentStore:R2:AccessKeyId"] = "test-access-key-id",
        ["ContentStore:R2:SecretAccessKey"] = "test-secret-access-key-not-real",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
