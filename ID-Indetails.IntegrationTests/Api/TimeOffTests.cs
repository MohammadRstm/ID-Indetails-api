using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.TimeOff;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.TimeOff.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Time off: Design employees ask, a super admin decides once, and while someone is off they log no hours and the super
/// admins hear when they are active. The clock is real, so days "today" are set straight in the database.
/// </summary>
public class TimeOffTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static DateOnly Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind).ToList();

    /// <summary>A week from <paramref name="daysAhead"/> days on: it always holds working days.</summary>
    private Task<TimeOffResponse> AskAsync(AuthResponse employee, int daysAhead = 10, int length = 7)
        => OkAsync<TimeOffResponse>(Client.PostJsonAsync("/api/me/time-off",
            new { firstDay = Today.AddDays(daysAhead), lastDay = Today.AddDays(daysAhead + length - 1), reason = "Family wedding" },
            employee.AccessToken));

    private Task<TimeOffResponse> ApproveAsync(AuthResponse superAdmin, long id, bool isPaid = true)
        => OkAsync<TimeOffResponse>(Client.PostJsonAsync($"/api/time-off/{id}/approve", new { isPaid }, superAdmin.AccessToken));

    /// <summary>Moves a request's days, straight in the database: the API only takes days from tomorrow on.</summary>
    private Task MoveAsync(long id, DateOnly first, DateOnly last)
        => _steps.ExecuteAsync("UPDATE time_off_requests SET first_day = @f, last_day = @l WHERE id = @id",
            ("@f", first.ToString("yyyy-MM-dd")), ("@l", last.ToString("yyyy-MM-dd")), ("@id", id));

    private async Task RunTwiceAsync()
    {
        for (var i = 0; i < 2; i++)
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(ScheduledCheck.TimeOff);
        }
    }

    [Fact]
    public async Task The_first_super_admin_decision_wins_and_the_employee_is_told()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var asked = await AskAsync(employee);
        asked.Status.ShouldBe(TimeOffStatus.Pending);
        asked.WorkingDays.ShouldBe(5);
        asked.Person.ShouldBeNull();
        (await OfKindAsync(superAdmin, NotificationKinds.TimeOffRequested)).ShouldContain(n => n.SubjectId == asked.Id);

        // two decisions at once: one goes through, the other is told it was already decided
        var both = await Task.WhenAll(
            Client.PostJsonAsync($"/api/time-off/{asked.Id}/approve", new { isPaid = true }, superAdmin.AccessToken),
            Client.PostJsonAsync($"/api/time-off/{asked.Id}/decline", new { note = "Busy week" }, superAdmin.AccessToken));
        both.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        await both.Single(r => !r.IsSuccessStatusCode).ShouldFailWithAsync(422, ErrorCodes.TimeOffDecided);

        var mine = (await OkAsync<List<TimeOffResponse>>(Client.GetAsync("/api/me/time-off", employee.AccessToken))).ShouldHaveSingleItem();
        mine.Status.ShouldBeOneOf(TimeOffStatus.Approved, TimeOffStatus.Declined);
        mine.DecidedByName.ShouldBeNull();
        (await OfKindAsync(employee, mine.Status == TimeOffStatus.Approved ? NotificationKinds.TimeOffApproved : NotificationKinds.TimeOffDeclined))
            .ShouldHaveSingleItem();
        await (await Client.PostEmptyAsync($"/api/me/time-off/{asked.Id}/withdraw", employee.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TimeOffDecided);
    }

    [Fact]
    public async Task Only_Design_employees_ask_and_only_super_admins_decide()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var body = new { firstDay = Today.AddDays(3), lastDay = Today.AddDays(9), reason = "Rest" };

        foreach (var someone in new[] { superAdmin, designAdmin, siteWorker })
        {
            ((int)(await Client.PostJsonAsync("/api/me/time-off", body, someone.AccessToken)).StatusCode).ShouldBe(403);
        }

        var asked = await AskAsync(employee);
        foreach (var someone in new[] { employee, designAdmin })
        {
            ((int)(await Client.PostJsonAsync($"/api/time-off/{asked.Id}/approve", new { isPaid = true }, someone.AccessToken)).StatusCode).ShouldBe(403);
            ((int)(await Client.GetAsync("/api/time-off", someone.AccessToken)).StatusCode).ShouldBe(403);
        }

        var pending = await OkAsync<PagedResponse<TimeOffResponse>>(Client.GetAsync("/api/time-off?view=pending&pageSize=100", superAdmin.AccessToken));
        var row = pending.Items.Single(r => r.Id == asked.Id);
        row.Person!.Id.ShouldBe(employee.User.Id);
        row.Reason.ShouldBe("Family wedding");
    }

    [Fact]
    public async Task Requests_start_tomorrow_need_a_reason_and_never_overlap()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.PostJsonAsync("/api/me/time-off", new { firstDay = Today, lastDay = Today.AddDays(6), reason = "Rest" }, employee.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.TimeOffTooSoon);
        await (await Client.PostJsonAsync("/api/me/time-off", new { firstDay = Today.AddDays(3), lastDay = Today.AddDays(9), reason = " " }, employee.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var asked = await AskAsync(employee);
        await (await Client.PostJsonAsync("/api/me/time-off",
                new { firstDay = asked.LastDay, lastDay = asked.LastDay.AddDays(6), reason = "More rest" }, employee.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TimeOffOverlaps);

        var withdrawn = await OkAsync<TimeOffResponse>(Client.PostEmptyAsync($"/api/me/time-off/{asked.Id}/withdraw", employee.AccessToken));
        withdrawn.Status.ShouldBe(TimeOffStatus.Withdrawn);
        (await OfKindAsync(superAdmin, NotificationKinds.TimeOffWithdrawn)).ShouldContain(n => n.SubjectId == asked.Id);
        (await AskAsync(employee)).Status.ShouldBe(TimeOffStatus.Pending);
    }

    [Fact]
    public async Task While_off_no_hours_are_logged_and_the_super_admins_are_alerted()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var asked = await AskAsync(employee);
        await ApproveAsync(superAdmin, asked.Id);
        await MoveAsync(asked.Id, Today.AddDays(-1), Today.AddDays(2));

        for (var i = 0; i < 2; i++)
        {
            await (await Work.TryLogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(3, 30), 30))
                .ShouldFailWithAsync(422, ErrorCodes.OnTimeOff);
        }

        var alert = (await OfKindAsync(superAdmin, NotificationKinds.TimeOffHours)).Where(n => n.SubjectId == employee.User.Id).ShouldHaveSingleItem();
        alert.Payload!.Value.GetProperty("userId").GetInt64().ShouldBe(employee.User.Id);

        // being active at all alerts them too, once a day
        for (var i = 0; i < 2; i++)
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITimeOffWatch>().SeenAsync(employee.User.Id);
        }

        (await OfKindAsync(superAdmin, NotificationKinds.TimeOffOnline)).Count(n => n.SubjectId == employee.User.Id).ShouldBe(1);

        // everyone sees who is off, never why nor whether it is paid
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var away = await Client.GetAsync("/api/time-off/away", colleague.AccessToken);
        var json = await away.Content.ReadAsStringAsync();
        json.ShouldNotContain("Family wedding");
        json.ShouldNotContain("isPaid");
        (await away.ReadAsync<List<AwayResponse>>()).ShouldContain(a => a.UserId == employee.User.Id && a.LastDay == Today.AddDays(2));
    }

    [Fact]
    public async Task Rejected_midway_the_days_before_stay_time_off_and_the_department_hears_they_are_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var asked = await AskAsync(employee);
        await ApproveAsync(superAdmin, asked.Id, isPaid: false);
        await MoveAsync(asked.Id, Today.AddDays(-3), Today.AddDays(3));

        var rejected = await OkAsync<TimeOffResponse>(Client.PostJsonAsync($"/api/time-off/{asked.Id}/reject", new { note = "Needed on site" }, superAdmin.AccessToken));
        rejected.Status.ShouldBe(TimeOffStatus.Rejected);
        rejected.EndedOn.ShouldBe(Today);
        rejected.IsPaid.ShouldBe(false);
        (await OfKindAsync(employee, NotificationKinds.TimeOffRejected)).ShouldHaveSingleItem();
        (await OfKindAsync(colleague, NotificationKinds.TimeOffBack)).ShouldContain(n => n.SubjectId == employee.User.Id);
        (await OfKindAsync(employee, NotificationKinds.TimeOffBack)).ShouldBeEmpty();

        // the days before today were taken, today is a work day again
        var away = await OkAsync<List<AwayResponse>>(Client.GetAsync($"/api/time-off/away?from={Today.AddDays(-3):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", colleague.AccessToken));
        away.Single(a => a.UserId == employee.User.Id).LastDay.ShouldBe(Today.AddDays(-1));
        await (await Client.PostJsonAsync($"/api/time-off/{asked.Id}/reject", new { }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TimeOffNotApproved);
    }

    [Fact]
    public async Task The_schedule_expires_undecided_requests_and_tells_the_department_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var undecided = await AskAsync(employee, daysAhead: 10);
        await MoveAsync(undecided.Id, Today, Today.AddDays(2));
        var approved = await AskAsync(employee, daysAhead: 20);
        await ApproveAsync(superAdmin, approved.Id);
        await MoveAsync(approved.Id, Today, Today);

        await RunTwiceAsync();

        (await OkAsync<List<TimeOffResponse>>(Client.GetAsync("/api/me/time-off", employee.AccessToken)))
            .Single(r => r.Id == undecided.Id).Status.ShouldBe(TimeOffStatus.Expired);
        (await OfKindAsync(employee, NotificationKinds.TimeOffExpired)).ShouldHaveSingleItem();
        (await OfKindAsync(colleague, NotificationKinds.TimeOffStarts)).Count(n => n.SubjectId == employee.User.Id).ShouldBe(1);
        (await OfKindAsync(superAdmin, NotificationKinds.TimeOffStarts)).Count(n => n.SubjectId == employee.User.Id).ShouldBe(1);
        (await OfKindAsync(employee, NotificationKinds.TimeOffStarts)).ShouldBeEmpty();
        await (await Client.PostJsonAsync($"/api/time-off/{undecided.Id}/approve", new { isPaid = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TimeOffDecided);
    }

    [Fact]
    public async Task Nobody_is_put_in_a_meeting_on_a_day_off()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var asked = await AskAsync(employee, daysAhead: 2, length: 7);
        await ApproveAsync(superAdmin, asked.Id);

        var startsAt = DateTime.UtcNow.Date.AddDays(4).AddHours(9);
        await (await Client.PostJsonAsync("/api/meetings",
                new { startsAt, endsAt = startsAt.AddHours(1), topic = "Review", domain = DomainCodes.Design, attendeeIds = new[] { employee.User.Id } },
                superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MeetingAttendeeOff);
    }

    [Fact]
    public async Task Leaving_cancels_pending_requests_and_ends_approved_time_off()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var approved = await AskAsync(employee, daysAhead: 5);
        await ApproveAsync(superAdmin, approved.Id);
        var pending = await AskAsync(employee, daysAhead: 20);

        (await Client.PostJsonAsync($"/api/employees/{employee.User.Id}/deactivate", new { }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await _steps.ScalarAsync<string>("SELECT status FROM time_off_requests WHERE id = @id", ("@id", approved.Id))).ShouldBe("ended");
        (await _steps.ScalarAsync<string>("SELECT status FROM time_off_requests WHERE id = @id", ("@id", pending.Id))).ShouldBe("cancelled");
        (await OfKindAsync(superAdmin, NotificationKinds.TimeOffEnded)).ShouldContain(n => n.SubjectId == employee.User.Id);
    }

    [Fact]
    public async Task A_day_of_time_off_is_never_excused_by_hand_too()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var asked = await AskAsync(employee);
        await ApproveAsync(superAdmin, asked.Id);
        await MoveAsync(asked.Id, Today, Today);

        await (await Client.PostJsonAsync($"/api/employees/{employee.User.Id}/hour-excuses",
                new { month = Today.ToString("yyyy-MM"), kind = "days", days = new[] { Today }, isPaid = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExcuseOnTimeOff);
        await (await Client.PostJsonAsync($"/api/employees/{employee.User.Id}/hour-excuses",
                new { month = Today.ToString("yyyy-MM"), kind = "whole_month", isPaid = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExcuseOnTimeOff);
    }
}
