using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The Design dashboard's "not logging" people with their hours this week, and the reminders sent to them: at most one a
/// day per person, from anyone. The tests use the real clock, so work is logged on today's company day.
/// </summary>
public class NotLoggingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    /// <summary>Half an hour of work early on today's company day, written straight to the database.</summary>
    private async Task LogTodayAsync(long userId, long projectId)
    {
        var midnight = TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut).Date, Beirut);
        await Work.InsertAsync(userId, projectId, midnight.AddMinutes(1).ToString("yyyy-MM-dd HH:mm:ss"), midnight.AddMinutes(31).ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<NotLoggingResponse> ListAsync(string accessToken)
        => await (await Client.GetAsync("/api/reports/not-logging", accessToken)).ReadAsync<NotLoggingResponse>();

    private async Task<List<NotificationResponse>> RemindersOfAsync(AuthResponse person)
        => (await (await Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)).ReadAsync<PagedResponse<NotificationResponse>>())
            .Items.Where(n => n.Kind == NotificationKinds.HoursReminder).ToList();

    [Fact]
    public async Task The_page_lists_the_people_the_dashboard_counts_with_their_hours()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, idle) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, busy) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await LogTodayAsync(busy.User.Id, project.Id);

        var page = await ListAsync(admin.AccessToken);

        page.WeekStart.ShouldBeLessThanOrEqualTo(page.Today);
        (page.Today.DayNumber - page.WeekStart.DayNumber).ShouldBeLessThan(7);
        var person = page.People.Single(p => p.Id == idle.User.Id);
        page.People.ShouldNotContain(p => p.Id == busy.User.Id);
        page.People.ShouldNotContain(p => p.Id == admin.User.Id);
        person.Email.ShouldNotBeNullOrWhiteSpace();
        person.RemindedToday.ShouldBeFalse();
        // nothing logged this week, and what there was to log comes from the hours ledger
        person.LoggedMinutes.ShouldBe(0);
        person.OvertimeMinutes.ShouldBe(0);
        person.ExpectedMinutes.ShouldNotBeNull();
        page.People.Select(p => p.Name).ShouldBe(page.People.Select(p => p.Name).Order().ToList());

        // the dashboard counts the same people
        var overview = await (await Client.GetAsync("/api/reports/overview", admin.AccessToken)).ReadAsync<OverviewResponse>();
        var item = overview.Attention.Single(a => a.Kind == AttentionKind.NotLogging);
        item.Count.ShouldBe(page.People.Count);
        item.People!.Select(p => p.Id).ShouldBe(page.People.Take(3).Select(p => p.Id));
    }

    [Fact]
    public async Task Only_those_who_see_the_dashboard_can_use_it_and_hours_need_to_see_everyones_work()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);

        await (await Client.GetAsync("/api/reports/not-logging", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync($"/api/reports/not-logging/{employee.User.Id}/reminder", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync("/api/reports/not-logging/reminders", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/reports/not-logging", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await RemindersOfAsync(employee)).ShouldBeEmpty();

        // colleagues' hours are only for those who hold work_entry:view_all
        (await ListAsync(admin.AccessToken)).People.Single(p => p.Id == employee.User.Id).LoggedMinutes.ShouldNotBeNull();
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WorkEntryViewAll, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var without = (await ListAsync(admin.AccessToken)).People.Single(p => p.Id == employee.User.Id);
        without.LoggedMinutes.ShouldBeNull();
        without.ExpectedMinutes.ShouldBeNull();
        without.OvertimeMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task A_reminder_names_the_sender_and_goes_once_a_day_from_anyone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, other) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var sent = await (await Client.PostEmptyAsync($"/api/reports/not-logging/{employee.User.Id}/reminder", admin.AccessToken))
            .ReadAsync<HoursRemindersResponse>();
        sent.ShouldBe(new HoursRemindersResponse(1, 0));

        var reminder = (await RemindersOfAsync(employee)).ShouldHaveSingleItem();
        reminder.Actor!.Id.ShouldBe(admin.User.Id);
        reminder.Payload!.Value.GetProperty("senderName").GetString().ShouldBe("Test admin");
        reminder.Payload!.Value.TryGetProperty("weekStart", out _).ShouldBeTrue();

        // the page shows it, and a second reminder the same day is refused, from the same admin or another
        (await ListAsync(other.AccessToken)).People.Single(p => p.Id == employee.User.Id).RemindedToday.ShouldBeTrue();
        await (await Client.PostEmptyAsync($"/api/reports/not-logging/{employee.User.Id}/reminder", admin.AccessToken))
            .ShouldFailWithAsync(409, "already_reminded");
        await (await Client.PostEmptyAsync($"/api/reports/not-logging/{employee.User.Id}/reminder", other.AccessToken))
            .ShouldFailWithAsync(409, "already_reminded");
        (await RemindersOfAsync(employee)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reminding_everyone_skips_those_already_reminded_and_nobody_to_remind_is_not_an_error()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, first) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, second) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/reports/not-logging/{first.User.Id}/reminder", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var listed = (await ListAsync(admin.AccessToken)).People.Count;

        var all = await (await Client.PostEmptyAsync("/api/reports/not-logging/reminders", admin.AccessToken)).ReadAsync<HoursRemindersResponse>();

        all.Skipped.ShouldBeGreaterThanOrEqualTo(1);
        (all.Reminded + all.Skipped).ShouldBe(listed);
        (await RemindersOfAsync(first)).ShouldHaveSingleItem();
        (await RemindersOfAsync(second)).ShouldHaveSingleItem();
        (await ListAsync(admin.AccessToken)).People.ShouldAllBe(p => p.RemindedToday);

        // everyone has been reminded: nothing to send, and nothing wrong
        var again = await (await Client.PostEmptyAsync("/api/reports/not-logging/reminders", admin.AccessToken)).ReadAsync<HoursRemindersResponse>();
        again.ShouldBe(new HoursRemindersResponse(0, listed));
        (await RemindersOfAsync(second)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Someone_who_logged_this_week_or_who_isnt_a_design_employee_is_not_reminded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, busy) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await LogTodayAsync(busy.User.Id, project.Id);

        var error = await (await Client.PostEmptyAsync($"/api/reports/not-logging/{busy.User.Id}/reminder", admin.AccessToken))
            .ShouldFailWithAsync(409, "logged_this_week");
        error.Message.ShouldBe("Test employee has already logged work this week.");
        await (await Client.PostEmptyAsync($"/api/reports/not-logging/{siteWorker.User.Id}/reminder", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync("/api/reports/not-logging/0/reminder", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await RemindersOfAsync(busy)).ShouldBeEmpty();
    }
}
