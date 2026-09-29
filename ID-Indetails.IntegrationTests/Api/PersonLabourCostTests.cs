using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class PersonLabourCostTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task A_profile_shows_what_someones_work_cost_this_quarter_all_time_and_by_project()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        // 30 an hour on a monthly salary (5,199.90 ÷ 173.33).
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "monthly", amount = 5199.90m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var brand = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var web = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        // Tuesday 10 February 2026 (Beirut is UTC+2): 2 hours on brand and 1 on web within hours, so the paid hour of
        // break is shared 40/20 minutes: brand 60 + 20, web 30 + 10.
        await Work.InsertAsync(anna.User.Id, brand.Id, "2026-02-10 07:00:00", "2026-02-10 09:00:00");
        await Work.InsertAsync(anna.User.Id, web.Id, "2026-02-10 11:00:00", "2026-02-10 12:00:00");

        // An evening hour on the first day of this quarter: outside hours, so no break, 30.
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, beirut));
        var quarterStart = new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1);
        var evening = TimeZoneInfo.ConvertTimeToUtc(quarterStart.ToDateTime(new TimeOnly(20, 0)), beirut);
        await Work.InsertAsync(anna.User.Id, brand.Id, evening.ToString("yyyy-MM-dd HH:mm:ss"), evening.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss"));

        var response = await Client.GetAsync($"/api/employees/{anna.User.Id}/labour-cost", admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var cost = await response.ReadAsync<EmployeeLabourCostResponse>();
        cost.EntryCount.ShouldBe(3);
        cost.ThisQuarter.Usd.ShouldBe(30m);
        cost.AllTime.Usd.ShouldBe(150m);
        cost.AllTime.UnpricedMinutes.ShouldBe(0);
        cost.ByProject.Select(p => (p.ProjectId, p.Cost.Usd)).ShouldBe([(brand.Id, 110m), (web.Id, 40m)]);
        cost.ByProject[0].Title.ShouldBe(brand.Title);

        await (await Client.GetAsync($"/api/employees/{anna.User.Id}/labour-cost", anna.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/employees/{anna.User.Id}/labour-cost", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/employees/999999/labour-cost", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
