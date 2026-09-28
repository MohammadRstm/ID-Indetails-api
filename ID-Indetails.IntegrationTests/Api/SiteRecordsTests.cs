using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Labour;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SiteRecordsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Site_staff_see_what_they_recorded_and_admins_see_it_too()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/materials",
            new { items = new[] { new { name = "Cement", amount = 100m }, new { name = "Sand", amount = 50m } } }, employee.AccessToken);
        var week = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", employee.AccessToken)).ReadAsync<LabourWeekListResponse>()).Weeks[0].WeekStart;
        var day = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks/{week:yyyy-MM-dd}", employee.AccessToken))
            .ReadAsync<LabourWeekResponse>()).Days[^1].Date;
        await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{week:yyyy-MM-dd}",
            new { days = new[] { new { date = day, workers = 10, dailyPay = 40m } } }, employee.AccessToken);

        var mine = await (await Client.GetAsync("/api/me/site-records", employee.AccessToken)).ReadAsync<SiteRecordsResponse>();
        mine.ShouldSatisfyAllConditions(
            r => r.Person.Id.ShouldBe(employee.User.Id),
            r => r.MaterialCount.ShouldBe(2),
            r => r.Materials.ShouldBe(150m),
            r => r.LabourWeeks.ShouldBe(1),
            r => r.Labour.ShouldBe(400m),
            r => r.Total.ShouldBe(550m),
            r => r.Weeks[^1].Value.ShouldBe(550m),
            r => r.Projects.Single().Title.ShouldBe(site.Title),
            r => r.Recent.Count.ShouldBe(3),
            r => r.Recent[0].Kind.ShouldBe(SiteRecords.Kind.LabourWeek));

        (await (await Client.GetAsync($"/api/employees/{employee.User.Id}/site-records", admin.AccessToken)).ReadAsync<SiteRecordsResponse>())
            .Total.ShouldBe(550m);
        await (await Client.GetAsync($"/api/employees/{employee.User.Id}/site-records", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/me/site-records", designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Site_cards_show_what_was_recorded_on_visible_sites_only()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var quiet = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Cement", amount = 100m } } }, employee.AccessToken);

        var cardsResponse = await Client.GetAsync($"/api/projects/site-summary?ids={site.Id}&ids={quiet.Id}&ids={design.Id}", employee.AccessToken);
        ((int)cardsResponse.StatusCode).ShouldBe(200, await cardsResponse.Content.ReadAsStringAsync());
        var cards = await cardsResponse.ReadAsync<List<SiteSummaryResponse>>();

        cards.Select(c => c.ProjectId).ShouldBe([site.Id, quiet.Id]);
        cards[0].ShouldSatisfyAllConditions(
            c => c.MaterialCount.ShouldBe(1),
            c => c.MaterialCost.ShouldBe(100m),
            c => c.MyLastRecordedAt.ShouldNotBeNull());
        cards[1].MyLastRecordedAt.ShouldBeNull();

        (await (await Client.GetAsync($"/api/projects/site-summary?ids={site.Id}", designAdmin.AccessToken)).ReadAsync<List<SiteSummaryResponse>>())
            .ShouldBeEmpty();
        await (await Client.GetAsync("/api/projects/site-summary", employee.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
