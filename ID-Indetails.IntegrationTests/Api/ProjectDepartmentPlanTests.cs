using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Planned hours per department on a Design project: set, changed and cleared with project:edit, each change in the
/// activity log with the value before, and progress by hours (logged ÷ planned) for whoever sees everyone's hours or the
/// money. Every test follows its own project, since the classes share one database.
/// </summary>
public class ProjectDepartmentPlanTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static string Url(long projectId) => $"/api/projects/{projectId}/departments/plan";

    /// <summary>A Design client project of 10,000 before TVA, with <paramref name="departmentIds"/>.</summary>
    private async Task<ProjectResponse> ProjectAsync(AuthResponse superAdmin, IReadOnlyList<long> departmentIds)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10000m,
            tvaRate = 0m,
            departmentIds,
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectDepartmentsResponse> PlanAsync(AuthResponse who, long projectId, params object[] departments)
    {
        var response = await Client.PutJsonAsync(Url(projectId), new { departments }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectDepartmentsResponse>();
    }

    private async Task<ProjectDepartmentsResponse> DepartmentsAsync(AuthResponse who, long projectId)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/departments", who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectDepartmentsResponse>();
    }

    private Task<long> PlanEventsAsync(long projectId)
        => _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'department_plan_set'", ("@p", projectId));

    /// <summary>3 hours in <paramref name="teamId"/> on Tuesday 10 February 2026, by someone paid 1,600 USD a month.</summary>
    private async Task LogThreeHoursAsync(AuthResponse superAdmin, long projectId, long teamId)
    {
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
                new { basis = "monthly", amount = 1_600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, team_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, @t, 'Drawings', '2026-02-10 07:00:00', '2026-02-10 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", projectId), ("@t", teamId));
    }

    [Fact]
    public async Task Planned_hours_are_set_changed_and_cleared_and_each_change_is_in_the_activity_with_the_value_before()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure " + Guid.NewGuid().ToString("N")[..6]);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await ProjectAsync(superAdmin, [architecture, structure.Id]);

        // nothing planned yet
        (await DepartmentsAsync(admin, project.Id)).Departments.ShouldAllBe(d => d.PlannedMinutes == null && d.Progress == null);

        // an admin who edits the project plans 120 hours for Architecture and 40.5 for Structure
        var set = await PlanAsync(admin, project.Id,
            new { teamId = architecture, plannedHours = 120m },
            new { teamId = structure.Id, plannedHours = 40.5m });
        set.Departments.Single(d => d.Id == architecture).PlannedMinutes.ShouldBe(7_200);
        set.Departments.Single(d => d.Id == structure.Id).PlannedMinutes.ShouldBe(2_430);
        (await PlanEventsAsync(project.Id)).ShouldBe(2);

        // changing one leaves the other as it was, and the same hours again change nothing
        var changed = await PlanAsync(admin, project.Id, new { teamId = architecture, plannedHours = 150m });
        changed.Departments.Single(d => d.Id == architecture).PlannedMinutes.ShouldBe(9_000);
        changed.Departments.Single(d => d.Id == structure.Id).PlannedMinutes.ShouldBe(2_430);
        await PlanAsync(admin, project.Id, new { teamId = architecture, plannedHours = 150m });
        (await PlanEventsAsync(project.Id)).ShouldBe(3);
        (await _steps.ScalarAsync<long>(
                "SELECT CAST(JSON_EXTRACT(payload, '$.prevPlannedMinutes') AS SIGNED) FROM activity_events " +
                "WHERE project_id = @p AND kind = 'department_plan_set' ORDER BY id DESC LIMIT 1", ("@p", project.Id)))
            .ShouldBe(7_200);
        (await _steps.ScalarAsync<long>(
                "SELECT CAST(JSON_EXTRACT(payload, '$.plannedMinutes') AS SIGNED) FROM activity_events " +
                "WHERE project_id = @p AND kind = 'department_plan_set' ORDER BY id DESC LIMIT 1", ("@p", project.Id)))
            .ShouldBe(9_000);
        (await _steps.ScalarAsync<long>(
                "SELECT actor_id FROM activity_events WHERE project_id = @p AND kind = 'department_plan_set' ORDER BY id DESC LIMIT 1",
                ("@p", project.Id)))
            .ShouldBe(admin.User.Id);

        // clearing Structure removes its plan, and the activity keeps what it was
        var cleared = await PlanAsync(admin, project.Id, new { teamId = structure.Id, plannedHours = (decimal?)null });
        cleared.Departments.Single(d => d.Id == structure.Id).PlannedMinutes.ShouldBeNull();
        cleared.Departments.Single(d => d.Id == structure.Id).Progress.ShouldBeNull();
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM project_department_plans WHERE project_id = @p", ("@p", project.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<long>(
                "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'department_plan_set' " +
                "AND JSON_TYPE(JSON_EXTRACT(payload, '$.plannedMinutes')) = 'NULL' " +
                "AND CAST(JSON_EXTRACT(payload, '$.prevPlannedMinutes') AS SIGNED) = 2430", ("@p", project.Id)))
            .ShouldBe(1);

        // taking Architecture off the project takes its plan with it, recorded the same way
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments", new { departmentIds = new[] { structure.Id } }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM project_department_plans WHERE project_id = @p", ("@p", project.Id))).ShouldBe(0);
        (await PlanEventsAsync(project.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task Progress_is_logged_over_planned_hours_for_whoever_sees_everyones_hours_and_planned_hours_for_everyone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure " + Guid.NewGuid().ToString("N")[..6]);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await ProjectAsync(superAdmin, [architecture, structure.Id]);
        await LogThreeHoursAsync(superAdmin, project.Id, architecture);

        await PlanAsync(superAdmin, project.Id, new { teamId = architecture, plannedHours = 4m });

        // with the money: 180 of 240 minutes is 0.75, beside the money that already counts the same minutes
        var withMoney = (await DepartmentsAsync(superAdmin, project.Id)).Departments.Single(d => d.Id == architecture);
        (withMoney.PlannedMinutes, withMoney.LoggedMinutes, withMoney.Progress).ShouldBe(((int?)240, (int?)180, (decimal?)0.75m));
        withMoney.Money!.Minutes.ShouldBe(180);

        // an admin who sees everyone's hours, not the money: the hours and progress, no money
        var hours = (await DepartmentsAsync(admin, project.Id)).Departments.Single(d => d.Id == architecture);
        (hours.PlannedMinutes, hours.LoggedMinutes, hours.Progress, hours.Money, hours.Share).ShouldBe(((int?)240, (int?)180, (decimal?)0.75m, (DepartmentMoneyResponse?)null, (decimal?)null));

        // an employee sees the plan, never the department's logged hours
        var plain = (await DepartmentsAsync(designer, project.Id)).Departments.Single(d => d.Id == architecture);
        (plain.PlannedMinutes, plain.LoggedMinutes, plain.Progress).ShouldBe(((int?)240, (int?)null, (decimal?)null));

        // a department without a plan has its logged minutes and no progress
        var unplanned = (await DepartmentsAsync(admin, project.Id)).Departments.Single(d => d.Id == structure.Id);
        (unplanned.PlannedMinutes, unplanned.LoggedMinutes, unplanned.Progress).ShouldBe(((int?)null, (int?)0, (decimal?)null));

        // past the plan, progress passes 1: 180 of 120 minutes
        var over = await PlanAsync(superAdmin, project.Id, new { teamId = architecture, plannedHours = 2m });
        over.Departments.Single(d => d.Id == architecture).Progress.ShouldBe(1.5m);

        // Financial Intelligence reads the same figures
        var insight = await (await Client.GetAsync($"/api/finance/insights/projects/{project.Id}", superAdmin.AccessToken)).ReadAsync<ProjectInsightResponse>();
        var row = insight.Departments.ShouldNotBeNull().Single(d => d.Id == architecture);
        (row.PlannedMinutes, row.Progress).ShouldBe(((int?)120, (decimal?)1.5m));
        insight.Departments!.Single(d => d.Id == structure.Id).PlannedMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task Only_a_design_project_s_own_departments_are_planned_by_whoever_edits_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure " + Guid.NewGuid().ToString("N")[..6]);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await ProjectAsync(superAdmin, [architecture]);
        var body = new { departments = new[] { new { teamId = architecture, plannedHours = (decimal?)10m } } };

        // a department not on the project, one twice, and hours out of range
        await (await Client.PutJsonAsync(Url(project.Id), new { departments = new[] { new { teamId = structure.Id, plannedHours = (decimal?)10m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(Url(project.Id), new
            {
                departments = new[] { new { teamId = architecture, plannedHours = (decimal?)10m }, new { teamId = architecture, plannedHours = (decimal?)12m } },
            }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(Url(project.Id), new { departments = new[] { new { teamId = architecture, plannedHours = (decimal?)0m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(Url(project.Id), new { departments = new[] { new { teamId = architecture, plannedHours = (decimal?)100_000.01m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(Url(project.Id), new { departments = Array.Empty<object>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // without project:edit, 403; a business the caller can't see, 404
        await (await Client.PutJsonAsync(Url(project.Id), body, designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync(Url(project.Id), body, executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // an Execution site has no departments
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await (await Client.PutJsonAsync(Url(site.Id), body, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);

        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM project_department_plans WHERE project_id = @p", ("@p", project.Id))).ShouldBe(0);
        (await PlanEventsAsync(project.Id)).ShouldBe(0);
    }
}
