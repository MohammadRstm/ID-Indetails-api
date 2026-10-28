using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class MeetingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    /// <summary>11:00 to 12:00 in Beirut, the day after tomorrow, so it never crosses midnight there.</summary>
    private static DateTime Start()
    {
        var day = DateTime.UtcNow.Date.AddDays(2);
        return DateTime.SpecifyKind(day.AddHours(8), DateTimeKind.Utc);
    }

    [Fact]
    public async Task Admins_set_move_and_remove_meetings_that_everyone_of_the_domain_sees()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = Start();

        var response = await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic = "  Kick-off  " }, admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var meeting = await response.ReadAsync<MeetingResponse>();
        meeting.Topic.ShouldBe("Kick-off");
        meeting.Minutes.ShouldBe(60);
        meeting.CreatedBy!.Id.ShouldBe(admin.User.Id);

        var moved = await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddMinutes(90), topic = "Kick-off" }, admin.AccessToken)).ReadAsync<MeetingResponse>();
        moved.Minutes.ShouldBe(90);
        moved.UpdatedAt.ShouldNotBeNull();

        (await (await Client.GetAsync($"/api/projects/{project.Id}/meetings", employee.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .Single().Id.ShouldBe(meeting.Id);
        (await (await Client.GetAsync("/api/meetings?mine=true", admin.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldContain(m => m.Id == meeting.Id);

        await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddHours(1) }, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/meetings/{meeting.Id}", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddMinutes(4) }, admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(20) }, admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await Client.DeleteAsync($"/api/meetings/{meeting.Id}", admin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await (await Client.GetAsync($"/api/projects/{project.Id}/meetings", employee.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldBeEmpty();
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'meeting' AND subject_id = @id", ("@id", meeting.Id))).ShouldBe(3);
    }

    [Fact]
    public async Task Employees_set_meetings_change_only_their_own_and_admins_are_told()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionEmployee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = Start();

        var response = await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic = "Design review" }, employee.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var mine = await response.ReadAsync<MeetingResponse>();

        Task<long> NotifiedAsync(long recipientId) => _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = 'meeting_scheduled' AND subject_id = @m",
            ("@r", recipientId), ("@m", mine.Id));
        (await NotifiedAsync(admin.User.Id)).ShouldBe(1);
        (await NotifiedAsync(superAdmin.User.Id)).ShouldBe(1);
        (await NotifiedAsync(employee.User.Id)).ShouldBe(0);
        (await NotifiedAsync(colleague.User.Id)).ShouldBe(0);

        (await (await Client.PutJsonAsync($"/api/meetings/{mine.Id}",
            new { startsAt = start, endsAt = start.AddMinutes(45), topic = "Design review" }, employee.AccessToken)).ReadAsync<MeetingResponse>())
            .Minutes.ShouldBe(45);
        await (await Client.PutJsonAsync($"/api/meetings/{mine.Id}",
            new { startsAt = start, endsAt = start.AddHours(1) }, colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/meetings/{mine.Id}", colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, executionEmployee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await Client.DeleteAsync($"/api/meetings/{mine.Id}", employee.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        // An admin's own meeting sends no notification.
        var adminMeeting = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ReadAsync<MeetingResponse>();
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE kind = 'meeting_scheduled' AND subject_id = @m", ("@m", adminMeeting.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Closed_projects_are_refused_and_other_domains_read_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionEmployee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var start = Start();
        var meeting = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ReadAsync<MeetingResponse>();

        await (await Client.GetAsync($"/api/projects/{project.Id}/meetings", executionEmployee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await (await Client.GetAsync("/api/meetings", executionEmployee.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldNotContain(m => m.Id == meeting.Id);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // On hold, a project takes no new meetings, but the ones it has can still be moved.
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotActive);
        (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddMinutes(30) }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/resume", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddHours(2) }, admin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);
    }

    private Task<long> NotifiedAsync(AuthResponse person, string kind, long meetingId) => _steps.ScalarAsync<long>(
        "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = @k AND subject_id = @m",
        ("@r", person.User.Id), ("@k", kind), ("@m", meetingId));

    private async Task<List<MeetingResponse>> ListAsync(AuthResponse person, string query = "")
        => await (await Client.GetAsync($"/api/meetings{query}", person.AccessToken)).ReadAsync<List<MeetingResponse>>();

    [Fact]
    public async Task A_general_meeting_is_for_its_attendees_and_its_domain_s_admins_who_hear_when_it_is_set_moved_or_cancelled()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, attendee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, bystander) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var start = Start();

        var response = await Client.PostJsonAsync("/api/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic = "Studio planning", attendeeIds = new[] { attendee.User.Id } }, admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var meeting = await response.ReadAsync<MeetingResponse>();
        meeting.Project.ShouldBeNull();
        meeting.Domain.ShouldBe(DomainCodes.Design);
        meeting.Attendees.ShouldHaveSingleItem().Id.ShouldBe(attendee.User.Id);

        (await NotifiedAsync(attendee, "meeting_invited", meeting.Id)).ShouldBe(1);
        (await NotifiedAsync(bystander, "meeting_invited", meeting.Id)).ShouldBe(0);
        (await NotifiedAsync(admin, "meeting_invited", meeting.Id)).ShouldBe(0);

        (await ListAsync(attendee)).ShouldContain(m => m.Id == meeting.Id);
        (await ListAsync(admin, "?domain=design")).ShouldContain(m => m.Id == meeting.Id);
        (await ListAsync(superAdmin)).ShouldContain(m => m.Id == meeting.Id);
        (await ListAsync(bystander)).ShouldNotContain(m => m.Id == meeting.Id);
        (await ListAsync(executionAdmin)).ShouldNotContain(m => m.Id == meeting.Id);

        // it is on the attendee's day, not on a colleague's
        var day = DateOnly.FromDateTime(start.AddHours(3));
        (await (await Client.GetAsync($"/api/me/day?date={day:yyyy-MM-dd}", attendee.AccessToken)).ReadAsync<MyDayResponse>())
            .Meetings.ShouldContain(m => m.Id == meeting.Id);
        (await (await Client.GetAsync($"/api/me/day?date={day:yyyy-MM-dd}", bystander.AccessToken)).ReadAsync<MyDayResponse>())
            .Meetings.ShouldNotContain(m => m.Id == meeting.Id);

        // moved: the attendee hears; then the bystander replaces them, who hears it is cancelled for them
        (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start.AddHours(1), endsAt = start.AddHours(2), topic = "Studio planning" }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await NotifiedAsync(attendee, "meeting_moved", meeting.Id)).ShouldBe(1);
        var swapped = await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start.AddHours(1), endsAt = start.AddHours(2), topic = "Studio planning", attendeeIds = new[] { bystander.User.Id } },
            admin.AccessToken)).ReadAsync<MeetingResponse>();
        swapped.Attendees.ShouldHaveSingleItem().Id.ShouldBe(bystander.User.Id);
        (await NotifiedAsync(bystander, "meeting_invited", meeting.Id)).ShouldBe(1);
        (await NotifiedAsync(attendee, "meeting_cancelled", meeting.Id)).ShouldBe(1);
        (await NotifiedAsync(bystander, "meeting_moved", meeting.Id)).ShouldBe(0);

        // an attendee can't change it, and whoever is no longer in it doesn't find it
        await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddHours(1) }, bystander.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/meetings/{meeting.Id}", attendee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await Client.DeleteAsync($"/api/meetings/{meeting.Id}", admin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await NotifiedAsync(bystander, "meeting_cancelled", meeting.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Company_wide_meetings_are_for_their_attendees_and_the_company_and_only_meeting_managers_set_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var start = Start();

        // a super admin may leave the domain out: the whole company
        var meeting = await (await Client.PostJsonAsync("/api/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic = "Partners", attendeeIds = new[] { admin.User.Id, executionAdmin.User.Id } },
            superAdmin.AccessToken)).ReadAsync<MeetingResponse>();
        meeting.Domain.ShouldBeNull();
        (await ListAsync(admin, "?domain=design")).ShouldContain(m => m.Id == meeting.Id);
        (await ListAsync(executionAdmin)).ShouldContain(m => m.Id == meeting.Id);
        (await ListAsync(employee)).ShouldNotContain(m => m.Id == meeting.Id);

        var body = new { startsAt = start, endsAt = start.AddHours(1), attendeeIds = new[] { employee.User.Id } };
        await (await Client.PostJsonAsync("/api/meetings", body, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/meetings", body with { attendeeIds = Array.Empty<long>() }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        // a Design admin can't invite site staff, nor set a meeting in Execution
        await (await Client.PostJsonAsync("/api/meetings", body with { attendeeIds = new[] { siteWorker.User.Id } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), domain = DomainCodes.Execution, attendeeIds = new[] { employee.User.Id } },
            admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // a project meeting has no attendees
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var projectMeeting = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ReadAsync<MeetingResponse>();
        projectMeeting.Domain.ShouldBe(DomainCodes.Design);
        projectMeeting.Attendees.ShouldBeEmpty();
        await (await Client.PutJsonAsync($"/api/meetings/{projectMeeting.Id}",
            new { startsAt = start, endsAt = start.AddHours(1), attendeeIds = new[] { employee.User.Id } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Execution_sites_have_meetings_like_design_projects()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        var start = Start();

        var response = await Client.PostJsonAsync($"/api/projects/{site.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic = "Slab inspection" }, siteWorker.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var meeting = await response.ReadAsync<MeetingResponse>();

        // The site's admin hears about it; a colleague can't move it; Design can't see it.
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = 'meeting_scheduled' AND subject_id = @m",
            ("@r", admin.User.Id), ("@m", meeting.Id))).ShouldBe(1);
        await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddMinutes(30) }, colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await (await Client.GetAsync($"/api/projects/{site.Id}/meetings", colleague.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldHaveSingleItem().Id.ShouldBe(meeting.Id);
        await (await Client.GetAsync($"/api/projects/{site.Id}/meetings", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await (await Client.GetAsync("/api/meetings", designAdmin.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldNotContain(m => m.Id == meeting.Id);

        // Someone who sees both domains can ask for one of them.
        (await (await Client.GetAsync("/api/meetings?domain=execution", superAdmin.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldContain(m => m.Id == meeting.Id);
        (await (await Client.GetAsync("/api/meetings?domain=design", superAdmin.AccessToken)).ReadAsync<List<MeetingResponse>>())
            .ShouldNotContain(m => m.Id == meeting.Id);
        await (await Client.GetAsync("/api/meetings?domain=nowhere", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task My_day_shows_my_entries_and_the_domains_meetings()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = Start();
        var meeting = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ReadAsync<MeetingResponse>();
        var day = DateOnly.FromDateTime(start.AddHours(3));

        var future = await (await Client.GetAsync($"/api/me/day?date={day:yyyy-MM-dd}", employee.AccessToken)).ReadAsync<MyDayResponse>();
        future.Meetings.ShouldContain(m => m.Id == meeting.Id);
        future.Entries.ShouldBeEmpty();

        var entry = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60);
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var entryDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(entry.StartedAt, beirut));
        var logged = await (await Client.GetAsync($"/api/me/day?date={entryDay:yyyy-MM-dd}", employee.AccessToken)).ReadAsync<MyDayResponse>();
        logged.Entries.Single().Id.ShouldBe(entry.Id);
        logged.LoggedMinutes.ShouldBe(60);
    }
}
