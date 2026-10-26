using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class WorkEntryScopeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task<WorkEntryResponse> OkAsync(HttpResponseMessage response, int status = 200)
    {
        ((int)response.StatusCode).ShouldBe(status, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WorkEntryResponse>();
    }

    [Fact]
    public async Task Whoever_logs_work_marks_it_outside_the_scope_and_an_edit_keeps_it_unless_it_says_otherwise()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgoOnOneDay(5, 45);

        var outside = await OkAsync(await Client.PostJsonAsync("/api/work-entries",
            new { projectId = project.Id, description = "A guest house the client added", startedAt = start, endedAt = start.AddMinutes(45), outsideScope = true },
            employee.AccessToken), 201);
        outside.OutsideScope.ShouldBeTrue();

        // an edit that doesn't say keeps it, as the timeline's moves and undo don't know about it
        var kept = await OkAsync(await Client.PutJsonAsync($"/api/work-entries/{outside.Id}",
            new { projectId = project.Id, description = "The guest house, first sketches", startedAt = start, endedAt = start.AddMinutes(45) }, employee.AccessToken));
        kept.OutsideScope.ShouldBeTrue();
        kept.Description.ShouldBe("The guest house, first sketches");

        var within = await OkAsync(await Client.PutJsonAsync($"/api/work-entries/{outside.Id}",
            new { projectId = project.Id, description = "Part of the brief after all", startedAt = start, endedAt = start.AddMinutes(45), outsideScope = false },
            employee.AccessToken));
        within.OutsideScope.ShouldBeFalse();

        // left out on a new entry, it is within the scope
        var plain = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(3, 30), 30, "Plans");
        plain.OutsideScope.ShouldBeFalse();
        (await _steps.ScalarAsync<int>("SELECT outside_scope FROM work_entries WHERE id = @id", ("@id", plain.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task An_admin_corrects_anyones_entry_after_the_edit_window_but_never_in_a_closed_month()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(4, 60), 60, "Facade options");
        // past the one-day edit window: the person can't change it any more
        await _steps.ScalarAsync<int>(
            "UPDATE work_entries SET created_at = DATE_SUB(created_at, INTERVAL 2 DAY) WHERE id = @id; SELECT 1", ("@id", entry.Id));

        var corrected = await OkAsync(await Client.PutJsonAsync($"/api/work-entries/{entry.Id}/scope", new { outsideScope = true }, admin.AccessToken));
        corrected.OutsideScope.ShouldBeTrue();
        corrected.Person.Id.ShouldBe(employee.User.Id);
        corrected.CanEdit.ShouldBeFalse();
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'work_entry' AND subject_id = @id AND kind = 'work_entry_updated' AND actor_id = @a",
            ("@id", entry.Id), ("@a", admin.User.Id))).ShouldBe(1);

        // the same value again changes nothing and records nothing
        (await OkAsync(await Client.PutJsonAsync($"/api/work-entries/{entry.Id}/scope", new { outsideScope = true }, superAdmin.AccessToken))).OutsideScope.ShouldBeTrue();
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'work_entry' AND subject_id = @id AND kind = 'work_entry_updated'", ("@id", entry.Id))).ShouldBe(1);

        // only people who see everyone's work, in Design
        await (await Client.PutJsonAsync($"/api/work-entries/{entry.Id}/scope", new { outsideScope = false }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/work-entries/{entry.Id}/scope", new { outsideScope = false }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync("/api/work-entries/999999999/scope", new { outsideScope = true }, admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a closed month never changes
        await Work.InsertAsync(employee.User.Id, project.Id, "2025-01-10 08:00:00", "2025-01-10 09:00:00", "Old drawings");
        var old = await _steps.ScalarAsync<long>(
            "SELECT id FROM work_entries WHERE user_id = @u AND description = 'Old drawings'", ("@u", employee.User.Id));
        await (await Client.PutJsonAsync($"/api/work-entries/{old}/scope", new { outsideScope = true }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
        (await _steps.ScalarAsync<int>("SELECT outside_scope FROM work_entries WHERE id = @id", ("@id", old))).ShouldBe(0);
    }

    [Fact]
    public async Task A_projects_work_says_how_much_of_it_was_outside_the_scope_and_whose()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, maya) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, omar) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var start = WorkEntryTestSteps.HoursAgoOnOneDay(6, 45);
        await OkAsync(await Client.PostJsonAsync("/api/work-entries",
            new { projectId = project.Id, description = "Extra bathroom", startedAt = start, endedAt = start.AddMinutes(45), outsideScope = true }, maya.AccessToken), 201);
        await Work.LogAsync(maya.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(3, 30), 30, "Plans");
        await Work.LogAsync(omar.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(4, 50), 50, "Elevations");

        var work = await (await Client.GetAsync($"/api/projects/{project.Id}/work", superAdmin.AccessToken)).ReadAsync<ProjectWorkResponse>();
        work.TotalMinutes.ShouldBe(125);
        work.OutsideScopeMinutes.ShouldBe(45);
        work.Contributors.Single(c => c.UserId == maya.User.Id).OutsideScopeMinutes.ShouldBe(45);
        work.Contributors.Single(c => c.UserId == omar.User.Id).OutsideScopeMinutes.ShouldBe(0);

        // and the timesheet says which entry it was
        var csv = await Client.GetAsync($"/api/work-entries/export?projectId={project.Id}", superAdmin.AccessToken);
        ((int)csv.StatusCode).ShouldBe(200, await csv.Content.ReadAsStringAsync());
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Single(l => l.Contains("Extra bathroom")).ShouldContain("Extra bathroom,Yes,");
        lines.Single(l => l.Contains("Elevations")).ShouldContain("Elevations,No,");
    }
}
