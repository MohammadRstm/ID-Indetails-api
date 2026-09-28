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
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 2000m, currency = "USD" }, superAdmin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Cement", amount = 200m } } }, superAdmin.AccessToken);
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
            p => p.ExpectedProfit.ShouldBe(820m),
            p => p.WorkingDays.ShouldBe(8),
            p => p.WorkingDaysOncePaid.ShouldBe(5));

        await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken);
        (await (await Client.GetAsync($"/api/projects/{site.Id}/site-money/timeline", superAdmin.AccessToken)).ReadAsync<SiteMoneyTimelineResponse>())
            .Projection.State.ShouldBe(LabourRules.ProjectionState.Done);
    }
}
