using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Labour;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SiteMoneyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task The_timeline_adds_up_cash_by_week_and_projects_when_profit_runs_out()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 2000m, currency = "USD" }, superAdmin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses", new { items = new[] { new { name = "Cement", amount = 200m } } }, superAdmin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/labour-defaults", new { workers = 5, dailyPay = 40m }, superAdmin.AccessToken);
        var week = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", superAdmin.AccessToken)).ReadAsync<LabourWeekListResponse>()).Weeks[0].WeekStart;
        var day = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks/{week:yyyy-MM-dd}", superAdmin.AccessToken))
            .ReadAsync<LabourWeekResponse>()).Days[^1].Date;
        await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{week:yyyy-MM-dd}",
            new { days = new[] { new { date = day, workers = 5, dailyPay = 40m } } }, superAdmin.AccessToken);

        var timeline = await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", superAdmin.AccessToken)).ReadAsync<SiteMoneyTimelineResponse>();

        timeline.Weeks.Single().ShouldBe(new CashWeekResponse(week, 2000m, 400m, 200m, 1600m, 2000m, 200m));
        timeline.Projection.ShouldSatisfyAllConditions(
            p => p.State.ShouldBe(LabourRules.ProjectionState.Ok),
            p => p.DailyCost.ShouldBe(200m),
            p => p.CashProfit.ShouldBe(1600m),
            p => p.ExpectedProfit.ShouldBe(600m),
            p => p.WorkingDays.ShouldBe(8),
            p => p.WorkingDaysOncePaid.ShouldBe(3));

        await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken);
        (await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", superAdmin.AccessToken)).ReadAsync<SiteMoneyTimelineResponse>())
            .Projection.State.ShouldBe(LabourRules.ProjectionState.Done);
    }

    [Fact]
    public async Task A_site_expense_counts_on_the_day_it_was_spent_not_the_day_it_was_added()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // priced well above the costs below, so the profit floor stays out of the way
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 5000m);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 1000m, currency = "USD" }, superAdmin.AccessToken);
        var added = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses", new
        {
            items = new object[]
            {
                new { name = "Cement", amount = 200m },
                new { name = "An old invoice", amount = 300m, spentOn = "2025-01-06" },
                new { name = "Booked far ahead", amount = 900m, spentOn = "2099-01-01" },
            },
        }, superAdmin.AccessToken);
        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync());

        var timeline = await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", superAdmin.AccessToken)).ReadAsync<SiteMoneyTimelineResponse>();

        // what was spent before the site's first week is in the running total; what is dated after this week isn't yet
        var week = timeline.Weeks.Single();
        week.Spent.ShouldBe(500m);
        week.Profit.ShouldBe(500m);
    }
}
