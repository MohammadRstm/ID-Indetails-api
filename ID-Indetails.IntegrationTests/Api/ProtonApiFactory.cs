using Anthropic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The API with Proton switched on, talking to <see cref="FakeAnthropic"/> instead of the real model.</summary>
public class ProtonApiFactory : ApiFactory
{
    public FakeAnthropic Anthropic { get; } = new();

    protected virtual decimal MonthlyBudgetUsd => 50m;

    protected virtual int MonthlyQuestionsPerUser => 300;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Proton:Enabled", "true");
        builder.UseSetting("Proton:ApiKey", "test-key");
        builder.UseSetting("Proton:MonthlyBudgetUsd", MonthlyBudgetUsd.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Proton:MonthlyQuestionsPerUser", MonthlyQuestionsPerUser.ToString());
        builder.UseSetting("RateLimiting:ProtonPermitsPerMinute", "10000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAnthropicClient>();
            services.AddSingleton<IAnthropicClient>(new AnthropicClient
            {
                ApiKey = "test-key",
                HttpClient = new HttpClient(Anthropic),
                MaxRetries = 0,
            });
        });
    }
}

/// <summary>Proton with voice switched on, talking to <see cref="FakeVoiceProvider"/> instead of the real provider.</summary>
public sealed class ProtonVoiceApiFactory : ProtonApiFactory
{
    public FakeVoiceProvider Voice { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Proton:Voice:Enabled", "true");
        builder.UseSetting("Proton:Voice:ApiKey", "voice-test-key");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ID_Indetails.api.Proton.Voice.IProtonVoiceProvider>();
            services.AddSingleton<ID_Indetails.api.Proton.Voice.IProtonVoiceProvider>(Voice);
        });
    }
}

/// <summary>No budget and two questions a person, to prove both limits.</summary>
public sealed class ProtonLimitsApiFactory : ProtonApiFactory
{
    protected override decimal MonthlyBudgetUsd => 0m;

    protected override int MonthlyQuestionsPerUser => 2;
}
