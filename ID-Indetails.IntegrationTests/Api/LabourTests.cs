using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class LabourTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Site_staff_report_a_week_from_the_defaults_and_admins_are_told()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution, staffIds: [employee.User.Id]);
        await SetDefaultsAsync(project.Id, admin.AccessToken, workers: 11, dailyPay: 45m);

        var list = await ListAsync(project.Id, employee.AccessToken);
        list.CanReport.ShouldBeTrue();
        var weekStart = list.Weeks.Single().WeekStart;
        var draft = await GetWeekAsync(project.Id, weekStart, employee.AccessToken);
        draft.IsReported.ShouldBeFalse();
        draft.Days.ShouldAllBe(d => d.Workers == 11 && d.DailyPay == 45m && d.Cost == 495m);

        var days = draft.Days.Select(d => new { date = d.Date, workers = 10, dailyPay = 40m }).ToArray();
        var week = await SubmitAsync(project.Id, weekStart, employee.AccessToken, days, note: "Pour day");
        week.IsReported.ShouldBeTrue();
        week.Total.ShouldBe(400m * days.Length);
        week.SubmittedBy!.Id.ShouldBe(employee.User.Id);
        week.CanEdit.ShouldBeTrue();

        (await ListAsync(project.Id, admin.AccessToken)).ShouldSatisfyAllConditions(
            l => l.Total.ShouldBe(400m * days.Length),
            l => l.ReportedWeeks.ShouldBe(1));
        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.Cost.GroundLabour.ShouldBe(400m * days.Length);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'labour_week_submitted' AND recipient_id = @r",
            ("@p", project.Id), ("@r", admin.User.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'labour_week_submitted' AND recipient_id = @r",
            ("@p", project.Id), ("@r", superAdmin.User.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task People_change_their_own_weeks_and_managers_anyone_s_keeping_the_submitter()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, first) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, second) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution, staffIds: [first.User.Id, second.User.Id]);
        var (weekStart, day) = await FirstDayAsync(project.Id, admin.AccessToken);

        await SubmitAsync(project.Id, weekStart, first.AccessToken, [new { date = day, workers = 5, dailyPay = 40m }]);

        (await GetWeekAsync(project.Id, weekStart, second.AccessToken)).CanEdit.ShouldBeFalse();
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
                new { days = new[] { new { date = day, workers = 1, dailyPay = 1m } } }, second.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var corrected = await SubmitAsync(project.Id, weekStart, admin.AccessToken, [new { date = day, workers = 6, dailyPay = 40m }]);
        corrected.Total.ShouldBe(240m);
        corrected.SubmittedBy!.Id.ShouldBe(first.User.Id);
        corrected.UpdatedAt.ShouldNotBeNull();

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind IN ('labour_week_submitted', 'labour_week_updated')",
            ("@p", project.Id))).ShouldBe(2);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM labour_week_days d JOIN labour_weeks w ON w.id = d.labour_week_id WHERE w.project_id = @p",
            ("@p", project.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Labour_defaults_are_admin_only_dated_today_and_replaced_the_same_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/labour-defaults", new { workers = 5, dailyPay = 30m }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await (await Client.GetAsync($"/api/projects/{project.Id}/labour-defaults", employee.AccessToken)).ReadAsync<LabourDefaultsResponse>())
            .ShouldSatisfyAllConditions(d => d.Current.ShouldBeNull(), d => d.CanEdit.ShouldBeFalse());

        await SetDefaultsAsync(project.Id, superAdmin.AccessToken, workers: 8, dailyPay: 42m);
        var defaults = await SetDefaultsAsync(project.Id, superAdmin.AccessToken, workers: 11, dailyPay: 45m);

        defaults.History.Count.ShouldBe(1);
        defaults.Current!.ShouldSatisfyAllConditions(
            c => c.Workers.ShouldBe(11),
            c => c.DailyPay.ShouldBe(45m),
            c => c.SetBy!.Id.ShouldBe(superAdmin.User.Id));
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'labour_defaults_set'", ("@p", project.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task A_site_can_be_created_with_its_labour_defaults()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var designClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);

        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Site with a crew", domainCode = DomainCodes.Execution, clientId = client.Id, projectFee = 1000m, materialMarkup = 0.1m,
            labourDefaults = new { workers = 8, dailyPay = 42m }, staffIds = new[] { await Projects.SiteStaffAsync() },
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();

        var defaults = await (await Client.GetAsync($"/api/projects/{project.Id}/labour-defaults", superAdmin.AccessToken)).ReadAsync<LabourDefaultsResponse>();
        defaults.Current!.ShouldSatisfyAllConditions(
            c => c.EffectiveFrom.ShouldBe(project.StartDate),
            c => c.Workers.ShouldBe(8),
            c => c.DailyPay.ShouldBe(42m));

        await (await Client.PostJsonAsync("/api/projects", new
            {
                title = "Design with a crew", domainCode = DomainCodes.Design, clientId = designClient.Id, revenue = 100m,
                labourDefaults = new { workers = 8, dailyPay = 42m },
            }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Weeks_and_days_outside_the_site_week_or_range_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (weekStart, day) = await FirstDayAsync(project.Id, superAdmin.AccessToken);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart.AddDays(1):yyyy-MM-dd}",
                new { days = new[] { new { date = day, workers = 1, dailyPay = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart.AddDays(7):yyyy-MM-dd}",
                new { days = new[] { new { date = weekStart.AddDays(7), workers = 1, dailyPay = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.LabourWeekOutOfRange);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
                new { days = new[] { new { date = weekStart.AddDays(6), workers = 1, dailyPay = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.LabourDayOutsideWeek);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
                new { days = new[] { new { date = day, workers = -1, dailyPay = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // Earlier weeks open up once the project started earlier.
        await _steps.ScalarAsync<int>("UPDATE projects SET start_date = DATE_SUB(start_date, INTERVAL 14 DAY) WHERE id = @p; SELECT 1;", ("@p", project.Id));
        var earlier = weekStart.AddDays(-7);
        (await SubmitAsync(project.Id, earlier, superAdmin.AccessToken, [new { date = earlier.AddDays(5), workers = 3, dailyPay = 50m }])).Total.ShouldBe(150m);
        (await ListAsync(project.Id, superAdmin.AccessToken)).Weeks.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_finalised_site_takes_no_labour_changes_until_reopened_admins_included()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (weekStart, day) = await FirstDayAsync(project.Id, superAdmin.AccessToken);
        await SubmitAsync(project.Id, weekStart, superAdmin.AccessToken, [new { date = day, workers = 5, dailyPay = 40m }]);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
                new { days = new[] { new { date = day, workers = 6, dailyPay = 40m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/labour-defaults", new { workers = 5, dailyPay = 30m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        (await ListAsync(project.Id, superAdmin.AccessToken)).CanReport.ShouldBeFalse();
        (await GetWeekAsync(project.Id, weekStart, superAdmin.AccessToken)).CanEdit.ShouldBeFalse();

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        (await SubmitAsync(project.Id, weekStart, superAdmin.AccessToken, [new { date = day, workers = 6, dailyPay = 40m }])).Total.ShouldBe(240m);
    }

    [Fact]
    public async Task Other_domains_never_see_site_labour_and_design_projects_have_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{site.Id}/labour-defaults", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // Labour permissions belong to Execution, so a Design admin is refused before any lookup.
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/labour-defaults", new { workers = 1, dailyPay = 1m }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{design.Id}/labour-weeks", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    /// <summary>The current week and its latest day that can be reported (today, or Saturday on a Sunday).</summary>
    private async Task<(DateOnly WeekStart, DateOnly Day)> FirstDayAsync(long projectId, string accessToken)
    {
        var weekStart = (await ListAsync(projectId, accessToken)).Weeks[0].WeekStart;
        return (weekStart, (await GetWeekAsync(projectId, weekStart, accessToken)).Days[^1].Date);
    }

    private async Task<LabourWeekListResponse> ListAsync(long projectId, string accessToken)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/labour-weeks", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LabourWeekListResponse>();
    }

    private async Task<LabourWeekResponse> GetWeekAsync(long projectId, DateOnly weekStart, string accessToken)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/labour-weeks/{weekStart:yyyy-MM-dd}", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LabourWeekResponse>();
    }

    private async Task<LabourWeekResponse> SubmitAsync(long projectId, DateOnly weekStart, string accessToken, object[] days, string? note = null)
    {
        var response = await Client.PutJsonAsync($"/api/projects/{projectId}/labour-weeks/{weekStart:yyyy-MM-dd}", new { days, note }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LabourWeekResponse>();
    }

    private async Task<LabourDefaultsResponse> SetDefaultsAsync(long projectId, string accessToken, int workers, decimal dailyPay)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/labour-defaults", new { workers, dailyPay }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LabourDefaultsResponse>();
    }
}
