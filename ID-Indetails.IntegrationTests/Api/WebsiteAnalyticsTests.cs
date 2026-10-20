using System.Text;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Services.GoogleAnalytics.Abstraction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Same API with Google Analytics switched on, answered by <see cref="ApiFactory.Analytics"/>.</summary>
public sealed class AnalyticsApiFactory : ApiFactory
{
    private const string Key = """{"type":"service_account","client_email":"ga@test.iam.gserviceaccount.com","private_key":"not a real key"}""";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("GoogleAnalytics:Enabled", "true");
        builder.UseSetting("GoogleAnalytics:PropertyId", "123456789");
        builder.UseSetting("GoogleAnalytics:ServiceAccountKeyBase64", Convert.ToBase64String(Encoding.UTF8.GetBytes(Key)));
        builder.UseSetting("RateLimiting:WebsiteAnalyticsPermitsPerMinute", "10000");
    }
}

/// <summary>The Website area's analytics (docs/website.md, "Analytics").</summary>
public class WebsiteAnalyticsTests : IClassFixture<AnalyticsApiFactory>
{
    private const string Analytics = "/api/website/analytics";

    private readonly AnalyticsApiFactory _api;
    private readonly AuthTestSteps _steps;

    public WebsiteAnalyticsTests(AnalyticsApiFactory api)
    {
        _api = api;
        _steps = new AuthTestSteps(api);
        // every test starts with nothing cached and Google answering
        ((MemoryCache)api.Services.GetRequiredService<IMemoryCache>()).Clear();
        api.Analytics.Reset();
    }

    private HttpClient Client => _steps.Client;

    private async Task GrantAsync(AuthResponse superAdmin, AuthResponse who, string code)
    {
        var grant = await Client.PutJsonAsync(
            $"/api/users/{who.User.Id}/permissions",
            new { changes = new[] { new { code, action = "grant", reason = "Integration test" } } },
            superAdmin.AccessToken);
        grant.IsSuccessStatusCode.ShouldBeTrue(await grant.Content.ReadAsStringAsync());
    }

    private async Task<WebsiteAnalyticsResponse> ReportAsync(AuthResponse who, string query = "")
    {
        var response = await Client.GetAsync($"{Analytics}{query}", who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WebsiteAnalyticsResponse>();
    }

    [Fact]
    public async Task Only_super_admins_website_staff_and_those_given_the_permission_see_the_analytics()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, websiteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Website);

        await (await Client.GetAsync(Analytics)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.GetAsync(Analytics, designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"{Analytics}/realtime", designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await ReportAsync(superAdmin)).HasData.ShouldBeFalse();
        await ReportAsync(websiteStaff);

        // seeing the website's content isn't seeing its visitors
        await GrantAsync(superAdmin, designAdmin, PermissionCodes.WebsiteView);
        await (await Client.GetAsync(Analytics, designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await GrantAsync(superAdmin, designAdmin, PermissionCodes.WebsiteAnalyticsView);
        await ReportAsync(designAdmin);
        var me = await (await Client.GetAsync("/api/auth/me", designAdmin.AccessToken)).ReadAsync<CurrentUserResponse>();
        me.VisibleDomains.ShouldContain(DomainCodes.Website);
    }

    [Fact]
    public async Task A_period_is_checked_then_answered_with_every_day_and_its_project_pages_named()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await (await Client.PostJsonAsync(
            "/api/website/projects",
            new { title = $"Villa Rizk {Guid.NewGuid():N}"[..20], type = "villa", status = "completed", year = 2022, city = "Beirut", country = "Lebanon", published = false },
            superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();

        var invalid = await (await Client.GetAsync($"{Analytics}?from=2026-10-01", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        invalid.Errors!.Keys.ShouldBe(["to"]);
        await (await Client.GetAsync($"{Analytics}?from=2026-10-07&to=2026-10-01", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"{Analytics}?from=2024-01-01&to=2026-01-01", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        _api.Analytics.Periods.ShouldBeEmpty();

        _api.Analytics.Report = FakeGoogleAnalytics.NothingRecorded with
        {
            Totals = new AnalyticsCounts(40, 30, 50, 20, 150, 2400, 900, 6),
            Days = [new AnalyticsDayCounts(new DateOnly(2026, 10, 2), 5, 6, 9)],
            Pages =
            [
                new WebsiteAnalyticsPage($"/projects/{project.Slug}", "Villa Rizk · ID-Indetails", null, 30, 10, 60, 80),
                new WebsiteAnalyticsPage("/projects/an-old-name", "Old · ID-Indetails", null, 3, 1, 5, 4),
            ],
            Events = [new WebsiteAnalyticsEvent(WebsiteAnalyticsRules.ContactRequestEvent, 3, 1, 3, true)],
        };

        var report = await ReportAsync(superAdmin, "?from=2026-10-01&to=2026-10-07");

        _api.Analytics.Periods.Single().ShouldBe(new AnalyticsPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 30)));
        report.HasData.ShouldBeTrue();
        report.Period.PreviousFrom.ShouldBe(new DateOnly(2026, 9, 24));
        report.Totals.EngagementRate.ShouldBe(0.4);
        report.Totals.ContactRequests.ShouldBe(3);
        report.PreviousTotals!.ContactRequests.ShouldBe(1);
        report.Days.Count.ShouldBe(7);
        report.Days.Single(d => d.Date == new DateOnly(2026, 10, 2)).Views.ShouldBe(9);
        report.Pages.Select(p => p.ProjectTitle).ShouldBe([project.Title, null]);

        // not compared: no previous period at all
        var alone = await ReportAsync(superAdmin, "?from=2026-10-01&to=2026-10-07&compare=false");
        alone.Period.PreviousFrom.ShouldBeNull();
        alone.Days.ShouldAllBe(d => d.PreviousDate == null);
    }

    [Fact]
    public async Task Answers_are_kept_and_Google_is_left_alone_for_a_minute_after_it_fails()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await ReportAsync(superAdmin);
        await ReportAsync(superAdmin);
        _api.Analytics.Periods.Count.ShouldBe(1);

        _api.Analytics.Down = true;
        await (await Client.GetAsync($"{Analytics}?from=2026-09-01&to=2026-09-07", superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.WebsiteAnalyticsUnavailable);
        _api.Analytics.Down = false;
        await (await Client.GetAsync($"{Analytics}?from=2026-08-01&to=2026-08-07", superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.WebsiteAnalyticsUnavailable);
        await (await Client.GetAsync($"{Analytics}/realtime", superAdmin.AccessToken)).ShouldFailWithAsync(503, ErrorCodes.WebsiteAnalyticsUnavailable);
        _api.Analytics.Periods.Count.ShouldBe(2);
        _api.Analytics.RealtimeCalls.ShouldBe(0);

        // what was answered before is still answered
        await ReportAsync(superAdmin);
    }

    [Fact]
    public async Task The_last_30_minutes_are_at_most_a_minute_old()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var perMinute = new long[30];
        perMinute[29] = 2;
        _api.Analytics.Realtime = new GoogleAnalyticsRealtime(2, perMinute, [new WebsiteRealtimePage("Contact · ID-Indetails", 3, 2)], [new WebsiteRealtimeCountry("LB", "Lebanon", 2)]);

        var now = await (await Client.GetAsync($"{Analytics}/realtime", superAdmin.AccessToken)).ReadAsync<WebsiteAnalyticsRealtimeResponse>();
        await Client.GetAsync($"{Analytics}/realtime", superAdmin.AccessToken);

        now.VisitorsNow.ShouldBe(2);
        now.VisitorsPerMinute.Count.ShouldBe(30);
        now.Pages.Single().Title.ShouldBe("Contact · ID-Indetails");
        _api.Analytics.RealtimeCalls.ShouldBe(1);
    }
}

/// <summary>Google Analytics left switched off, as in a fresh install.</summary>
public class WebsiteAnalyticsOffTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Until_Google_Analytics_is_connected_the_analytics_say_so()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await _steps.Client.GetAsync("/api/website/analytics", superAdmin.AccessToken)).ShouldFailWithAsync(503, ErrorCodes.WebsiteAnalyticsNotConfigured);
        await (await _steps.Client.GetAsync("/api/website/analytics/realtime", superAdmin.AccessToken)).ShouldFailWithAsync(503, ErrorCodes.WebsiteAnalyticsNotConfigured);
        api.Analytics.Periods.ShouldBeEmpty();
    }
}
