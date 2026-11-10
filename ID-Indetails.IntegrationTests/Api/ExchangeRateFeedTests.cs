using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The API with the daily exchange rate feed answered by <see cref="Feed"/>, for LBP, EGP and KES. The job stays off, as in
/// every test: the feed service is run by hand.
/// </summary>
public sealed class ExchangeRateFeedApiFactory : ApiFactory
{
    public FakeExchangeRateFeed Feed { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // these replace appsettings.json's list (LBP) position by position
        builder.UseSetting("ExchangeRateFeed:Currencies:0", "EGP");
        builder.UseSetting("ExchangeRateFeed:Currencies:1", "LBP");
        builder.UseSetting("ExchangeRateFeed:Currencies:2", "KES");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExchangeRateFeed>();
            services.AddSingleton<IExchangeRateFeed>(Feed);
        });
    }
}

/// <summary>
/// The daily exchange rate feed, with the source answered by the test. Every run goes over all three currencies and each
/// step builds on the rates the one before left, so the story is one test, and the class's database holds only its rates.
/// </summary>
public class ExchangeRateFeedTests(ExchangeRateFeedApiFactory api) : IClassFixture<ExchangeRateFeedApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<int> AddTodaysRatesAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IExchangeRateFeedService>().AddTodaysRatesAsync();
    }

    /// <summary>Newest first.</summary>
    private async Task<List<ExchangeRateResponse>> RatesAsync(string currency, string token)
    {
        var response = await Client.GetAsync($"/api/exchange-rates?currency={currency}", token);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<ExchangeRateResponse>>();
    }

    [Fact]
    public async Task Todays_rate_is_added_once_by_no_one_and_a_jump_or_a_missing_quote_keeps_the_last_rate()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);

        // LBP has a person's rate from yesterday; EGP and KES have none
        var added = await Client.PostJsonAsync("/api/exchange-rates",
            new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = today.AddDays(-1).ToString("yyyy-MM-dd") }, token);
        ((int)added.StatusCode).ShouldBe(201, await added.Content.ReadAsStringAsync());
        var lbpYesterday = await added.ReadAsync<ExchangeRateResponse>();

        // the source quotes EGP, LBP half as much again as yesterday (more than 10% away: a glitch), and no KES
        api.Feed.Quotes["EGP"] = 50.55m;
        api.Feed.Quotes["LBP"] = 134_250m;
        (await AddTodaysRatesAsync()).ShouldBe(1);
        api.Feed.Calls.ShouldBe(1);

        var egp = (await RatesAsync("EGP", token)).ShouldHaveSingleItem();
        egp.RatePerUsd.ShouldBe(50.55m);
        egp.EffectiveFrom.ShouldBe(today);
        egp.Automatic.ShouldBeTrue();
        var lbp = (await RatesAsync("LBP", token)).ShouldHaveSingleItem();
        lbp.Id.ShouldBe(lbpYesterday.Id);
        lbp.RatePerUsd.ShouldBe(89_500m);
        lbp.Automatic.ShouldBeFalse();
        (await RatesAsync("KES", token)).ShouldBeEmpty();

        // later that day the source quotes LBP within 10% of yesterday's rate, and KES: both are added, EGP keeps today's
        api.Feed.Quotes["LBP"] = 90_000m;
        api.Feed.Quotes["KES"] = 129.25m;
        (await AddTodaysRatesAsync()).ShouldBe(2);
        api.Feed.Calls.ShouldBe(2);
        (await RatesAsync("LBP", token)).Select(r => (r.RatePerUsd, r.EffectiveFrom, r.Automatic))
            .ShouldBe([(90_000m, today, true), (89_500m, today.AddDays(-1), false)]);
        var kes = (await RatesAsync("KES", token)).ShouldHaveSingleItem();
        (kes.RatePerUsd, kes.EffectiveFrom, kes.Automatic).ShouldBe((129.25m, today, true));
        (await RatesAsync("EGP", token)).ShouldHaveSingleItem().Id.ShouldBe(egp.Id);

        // every currency has a rate from today: the source isn't asked again
        (await AddTodaysRatesAsync()).ShouldBe(0);
        api.Feed.Calls.ShouldBe(2);

        // each rate the feed added is a company event with no one behind it, naming the source; the skipped quote left none
        var feed = await (await Client.GetAsync("/api/activity?group=billing&pageSize=100", token)).ReadAsync<PagedResponse<ActivityItemResponse>>();
        var rateEvents = feed.Items.Where(i => i.Kind == ActivityEventKinds.ExchangeRateSet).ToList();
        rateEvents.Count.ShouldBe(4);
        var egpSet = rateEvents.Single(i => i.SubjectId == egp.Id);
        egpSet.Actor.ShouldBeNull();
        egpSet.Domain.ShouldBeNull();
        var payload = egpSet.Payload!.Value;
        payload.GetProperty("currency").GetString().ShouldBe("EGP");
        payload.GetProperty("automatic").GetBoolean().ShouldBeTrue();
        payload.GetProperty("source").GetString().ShouldBe("ExchangeRate-API");
        rateEvents.Single(i => i.SubjectId == lbpYesterday.Id).Actor.ShouldNotBeNull().Id.ShouldBe(superAdmin.User.Id);
    }
}
