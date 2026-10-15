using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.ContentStore.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.ContentStore;

/// <summary>What the settings allow, and which store they choose.</summary>
public class ContentStoreSetupTests
{
    private static readonly (string Key, string Value)[] FullR2 =
    [
        ("ContentStore:Provider", "R2"),
        ("ContentStore:R2:AccountId", "0123456789abcdef0123456789abcdef"),
        ("ContentStore:R2:Bucket", "portal-test"),
        ("ContentStore:R2:AccessKeyId", "test-access-key-id"),
        ("ContentStore:R2:SecretAccessKey", "test-secret-access-key-not-real"),
    ];

    private static ServiceProvider Build(string environmentName, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(environment.Object)
            .AddSingleton(Mock.Of<IClock>())
            .AddContentStore(configuration, environment.Object)
            .BuildServiceProvider();
    }

    private static string[] FailuresOf(ServiceProvider provider)
        => Should.Throw<OptionsValidationException>(() => { _ = provider.GetRequiredService<IOptions<ContentStoreOptions>>().Value; })
            .Failures.ToArray();

    [Fact]
    public void Nothing_set_means_no_store()
    {
        using var provider = Build("Production");

        provider.GetRequiredService<IOptions<ContentStoreOptions>>().Value.Provider.ShouldBe(ContentStoreProvider.None);
        provider.GetRequiredService<IContentStore>().ShouldBeOfType<UnconfiguredContentStore>();
        provider.GetService<LocalContentStore>().ShouldBeNull();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_is_allowed_outside_production_and_is_also_served_by_its_own_type(string environmentName)
    {
        using var provider = Build(environmentName, ("ContentStore:Provider", "Local"));

        provider.GetRequiredService<IOptions<ContentStoreOptions>>().Value.Local.RootPath.ShouldBe("App_Data/portal-content");
        provider.GetRequiredService<IContentStore>().ShouldBeSameAs(provider.GetRequiredService<LocalContentStore>());
    }

    [Fact]
    public void Local_is_refused_in_production()
    {
        using var provider = Build("Production", ("ContentStore:Provider", "Local"));

        var failures = FailuresOf(provider);

        failures.ShouldHaveSingleItem().ShouldContain("can't be Local in production");
    }

    [Fact]
    public void R2_with_every_setting_is_chosen_in_production_and_leaves_no_local_store()
    {
        using var provider = Build("Production", FullR2);

        provider.GetRequiredService<IContentStore>().ShouldBeOfType<R2ContentStore>();
        provider.GetService<LocalContentStore>().ShouldBeNull();
    }

    [Fact]
    public void R2_without_its_settings_is_refused_naming_each_missing_one()
    {
        using var provider = Build("Production", ("ContentStore:Provider", "R2"));

        var failures = FailuresOf(provider);

        failures.Length.ShouldBe(4);
        foreach (var field in new[] { "AccountId", "Bucket", "AccessKeyId", "SecretAccessKey" })
        {
            failures.ShouldContain(f => f.Contains($"ContentStore:R2:{field} is required"));
        }
    }

    [Theory]
    [InlineData("ContentStore:R2:Bucket", "Bucket")]
    [InlineData("ContentStore:R2:SecretAccessKey", "SecretAccessKey")]
    public void R2_with_one_setting_blank_is_refused(string blankKey, string field)
    {
        using var provider = Build("Production", FullR2.Select(s => s.Key == blankKey ? (s.Key, "  ") : s).ToArray());

        FailuresOf(provider).ShouldHaveSingleItem().ShouldContain($"ContentStore:R2:{field}");
    }

    [Fact]
    public void An_endpoint_override_must_be_https()
    {
        using var provider = Build("Production", [.. FullR2, ("ContentStore:R2:Endpoint", "http://storage.example.test")]);

        FailuresOf(provider).ShouldHaveSingleItem().ShouldContain("ContentStore:R2:Endpoint must be an https URL");
    }

    [Fact]
    public void R2_settings_are_not_needed_for_the_other_providers()
    {
        using var none = Build("Production", ("ContentStore:Provider", "None"));
        using var local = Build("Development", ("ContentStore:Provider", "Local"));

        none.GetRequiredService<IOptions<ContentStoreOptions>>().Value.Provider.ShouldBe(ContentStoreProvider.None);
        local.GetRequiredService<IOptions<ContentStoreOptions>>().Value.Provider.ShouldBe(ContentStoreProvider.Local);
    }

    [Fact]
    public void The_unconfigured_store_is_unavailable_and_every_call_answers_503()
    {
        var store = new UnconfiguredContentStore();
        Action[] calls =
        [
            () => store.StartUploadAsync("k", "text/plain"),
            () => store.GetPartUploadUrlAsync("k", "u", 1, DateTime.UtcNow),
            () => store.ListPartsAsync("k", "u"),
            () => store.CompleteUploadAsync("k", "u", []),
            () => store.AbortUploadAsync("k", "u"),
            () => store.GetSizeAsync("k"),
            () => store.ReadStartAsync("k", 10),
            () => store.GetDownloadUrlAsync("k", "a.pdf", "application/pdf", true, DateTime.UtcNow),
            () => store.DeleteAsync("k"),
            () => store.ListAsync("p"),
        ];

        store.IsAvailable.ShouldBeFalse();
        foreach (var call in calls)
        {
            var failure = Should.Throw<ServiceUnavailableException>(call);
            failure.Code.ShouldBe(ErrorCodes.ContentStorageUnavailable);
            failure.Message.ShouldBe("Client portal file storage isn't set up yet. Ask your administrator to configure it.");
        }
    }
}
