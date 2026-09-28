using ID_Indetails.api.Constants;
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
            new { startsAt = start, endsAt = start.AddHours(1) }, executionEmployee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await Client.DeleteAsync($"/api/meetings/{mine.Id}", employee.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        // An admin's own meeting sends no notification.
        var adminMeeting = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, admin.AccessToken)).ReadAsync<MeetingResponse>();
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE kind = 'meeting_scheduled' AND subject_id = @m", ("@m", adminMeeting.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Closed_projects_and_other_domains_are_refused()
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
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/meetings",
            new { startsAt = start, endsAt = start.AddHours(1) }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PutJsonAsync($"/api/meetings/{meeting.Id}",
            new { startsAt = start, endsAt = start.AddHours(2) }, admin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);
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
