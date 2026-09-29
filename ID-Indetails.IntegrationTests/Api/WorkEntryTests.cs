using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class WorkEntryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task An_employee_logs_edits_deletes_and_restores_their_own_work()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(5);

        var entry = await Work.LogAsync(employee.AccessToken, project.Id, start, 90, "  Moodboard  ");
        entry.Description.ShouldBe("Moodboard");
        entry.Minutes.ShouldBe(90);
        entry.CanEdit.ShouldBeTrue();
        entry.Person.Id.ShouldBe(employee.User.Id);
        entry.Project.Title.ShouldBe(project.Title);

        var edited = await (await Client.PutJsonAsync($"/api/work-entries/{entry.Id}",
            new { projectId = project.Id, description = "Moodboard v2", startedAt = start, endedAt = start.AddHours(2) },
            employee.AccessToken)).ReadAsync<WorkEntryResponse>();
        edited.Minutes.ShouldBe(120);
        edited.UpdatedAt.ShouldNotBeNull();

        (await Client.DeleteAsync($"/api/work-entries/{entry.Id}", employee.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.DeleteAsync($"/api/work-entries/{entry.Id}", employee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var restored = await (await Client.PostEmptyAsync($"/api/work-entries/{entry.Id}/restore", employee.AccessToken)).ReadAsync<WorkEntryResponse>();
        restored.Id.ShouldBe(entry.Id);

        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'work_entry' AND subject_id = @id", ("@id", entry.Id))).ShouldBe(4);
    }

    [Fact]
    public async Task Overlaps_future_ends_missing_descriptions_and_closed_projects_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(6);
        await Work.LogAsync(employee.AccessToken, project.Id, start, 60);

        await (await Work.TryLogAsync(employee.AccessToken, project.Id, start.AddMinutes(30), 60)).ShouldFailWithAsync(422, ErrorCodes.EntryOverlaps);
        await Work.LogAsync(employee.AccessToken, project.Id, start.AddHours(1), 60);
        await (await Work.TryLogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(1), 120)).ShouldFailWithAsync(422, ErrorCodes.EntryInFuture);
        await (await Work.TryLogAsync(employee.AccessToken, project.Id, start.AddHours(3), 30, " ")).ShouldFailWithAsync(400, ErrorCodes.DescriptionRequired);
        await (await Work.TryLogAsync(employee.AccessToken, project.Id, start.AddHours(3), 4)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Work.TryLogAsync(employee.AccessToken, project.Id, start.AddHours(3), 30)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotAcceptingWork);
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, holdLogging: true);
        await Work.LogAsync(employee.AccessToken, project.Id, start.AddHours(3), 30);

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Work.TryLogAsync(employee.AccessToken, project.Id, start.AddHours(4), 30)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotAcceptingWork);

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, allowOverlap: true, allowFutureEntries: true, requireDescription: false);
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.LogAsync(employee.AccessToken, other.Id, start, 60, "");
        await Work.LogAsync(employee.AccessToken, other.Id, WorkEntryTestSteps.HoursAgo(1), 120);
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
    }

    [Fact]
    public async Task Moving_entries_to_make_room_is_checked_and_saved_together()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(6);
        var first = await Work.LogAsync(employee.AccessToken, project.Id, start, 60);

        // The new entry takes the first hour; the old one moves to the second.
        var response = await Client.PostJsonAsync("/api/work-entries", new
        {
            projectId = project.Id,
            description = "Swap in",
            startedAt = start,
            endedAt = start.AddHours(1),
            moves = new[] { new { id = first.Id, startedAt = start.AddHours(1), endedAt = start.AddHours(2) } },
        }, employee.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());

        (await _steps.ScalarAsync<DateTime>("SELECT started_at FROM work_entries WHERE id = @id", ("@id", first.Id)))
            .ShouldBe(start.AddHours(1));

        // A move that still clashes changes nothing.
        await (await Client.PostJsonAsync("/api/work-entries", new
        {
            projectId = project.Id,
            description = "Clash",
            startedAt = start.AddHours(2),
            endedAt = start.AddHours(3),
            moves = new[] { new { id = first.Id, startedAt = start.AddMinutes(90), endedAt = start.AddMinutes(150) } },
        }, employee.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.EntryOverlaps);
        (await _steps.ScalarAsync<DateTime>("SELECT started_at FROM work_entries WHERE id = @id", ("@id", first.Id)))
            .ShouldBe(start.AddHours(1));
    }

    [Fact]
    public async Task Entries_past_the_edit_window_are_locked()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(3);
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, start, 60);
        await _steps.ScalarAsync<int>(
            "UPDATE work_entries SET created_at = DATE_SUB(created_at, INTERVAL 2 DAY) WHERE id = @id; SELECT 1", ("@id", entry.Id));

        await (await Client.PutJsonAsync($"/api/work-entries/{entry.Id}",
            new { projectId = project.Id, description = "Late", startedAt = start, endedAt = start.AddHours(2) }, employee.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.EditWindowClosed);
        await (await Client.DeleteAsync($"/api/work-entries/{entry.Id}", employee.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.EditWindowClosed);

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, editWindow: "seven_days");
        (await Client.DeleteAsync($"/api/work-entries/{entry.Id}", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
    }

    [Fact]
    public async Task Nobody_changes_someone_elses_entries_and_other_domains_are_out_of_reach()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionEmployee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var start = WorkEntryTestSteps.HoursAgo(6);
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, start, 60);

        var body = new { projectId = project.Id, description = "Mine now", startedAt = start, endedAt = start.AddHours(1) };
        await (await Client.PutJsonAsync($"/api/work-entries/{entry.Id}", body, colleague.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/work-entries/{entry.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        await (await Work.TryLogAsync(executionEmployee.AccessToken, project.Id, start, 60)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Work.TryLogAsync(designAdmin.AccessToken, site.Id, start, 60)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Work.TryLogAsync(superAdmin.AccessToken, site.Id, start, 60)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task Admins_are_notified_per_entry_or_in_a_daily_digest()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(8);

        Task<long> CountAsync(long recipientId, string kind) => _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = @k", ("@r", recipientId), ("@k", kind));

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, notifyEntry: "all");
        await Work.LogAsync(employee.AccessToken, project.Id, start, 30);
        (await CountAsync(admin.User.Id, NotificationKinds.WorkLogged)).ShouldBe(1);
        (await CountAsync(superAdmin.User.Id, NotificationKinds.WorkLogged)).ShouldBe(1);
        (await CountAsync(employee.User.Id, NotificationKinds.WorkLogged)).ShouldBe(0);
        (await CountAsync(executionAdmin.User.Id, NotificationKinds.WorkLogged)).ShouldBe(0);

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, notifyEntry: "long");
        await Work.LogAsync(employee.AccessToken, project.Id, start.AddHours(1), 60);
        (await CountAsync(admin.User.Id, NotificationKinds.WorkLogged)).ShouldBe(1);

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, notifyEntry: "digest");
        await Work.LogAsync(employee.AccessToken, project.Id, start.AddHours(2), 30);
        await Work.LogAsync(employee.AccessToken, project.Id, start.AddHours(3), 45);
        (await CountAsync(admin.User.Id, NotificationKinds.WorkDigest)).ShouldBe(1);
        (await _steps.ScalarAsync<int>(
            "SELECT count FROM notifications WHERE recipient_id = @r AND kind = 'work_digest'", ("@r", admin.User.Id))).ShouldBe(2);
        (await _steps.ScalarAsync<int>(
            "SELECT JSON_EXTRACT(payload, '$.minutes') FROM notifications WHERE recipient_id = @r AND kind = 'work_digest'", ("@r", admin.User.Id)))
            .ShouldBe(75);

        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
    }
}
