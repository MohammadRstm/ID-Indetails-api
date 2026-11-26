using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.TimeOff;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Hours and time off (decided 26 November 2026): while someone is off they log on every other day as always, and hours
/// they put on a day of their time off wait for a super admin, who approves them as overtime or as normal hours, or turns
/// them down. Each test's employee is off from yesterday to two days ahead, set straight in the database. Reopened months
/// are company-wide, so the reopened-month test uses July 2024, which no other test logs in, and locks it again after.
/// </summary>
public class TimeOffHoursTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string ReopenedMonth = "2024-07";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static DateOnly Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);

    // Beirut is UTC+3 in July: 06:00 UTC is 09:00. Tuesday 9 July 2024.
    private static DateTime InJuly(int hourUtc = 6) => new(2024, 7, 9, hourUtc, 0, 0, DateTimeKind.Utc);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind).ToList();

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => _steps.ScalarAsync<long>(sql, parameters);

    private Task<long> EntriesAsync(long userId)
        => CountAsync("SELECT COUNT(*) FROM work_entries WHERE user_id = @u AND deleted_at IS NULL", ("@u", userId));

    /// <summary>A super admin, a Design employee off from yesterday to two days ahead, and a Design project.</summary>
    private async Task<(AuthResponse SuperAdmin, AuthResponse Employee, long ProjectId)> OffTodayAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var asked = await OkAsync<TimeOffResponse>(Client.PostJsonAsync("/api/me/time-off",
            new { firstDay = Today.AddDays(10), lastDay = Today.AddDays(16), reason = "Family wedding" }, employee.AccessToken));
        await OkAsync<TimeOffResponse>(Client.PostJsonAsync($"/api/time-off/{asked.Id}/approve", new { isPaid = true }, superAdmin.AccessToken));
        await _steps.ExecuteAsync("UPDATE time_off_requests SET first_day = @f, last_day = @l WHERE id = @id",
            ("@f", Today.AddDays(-1).ToString("yyyy-MM-dd")), ("@l", Today.AddDays(2).ToString("yyyy-MM-dd")), ("@id", asked.Id));
        return (superAdmin, employee, project.Id);
    }

    /// <summary>Half an hour logged today, a day of the employee's time off: held, with the kept change's id.</summary>
    private async Task<long> HeldAsync(AuthResponse employee, long projectId)
        => (await (await Work.TryLogAsync(employee.AccessToken, projectId, WorkEntryTestSteps.HoursAgoOnOneDay(3, 30), 30))
            .ShouldFailWithAsync(422, ErrorCodes.TimeOffHoursHeld)).BlockedChangeId.ShouldNotBeNull();

    private Task<HttpResponseMessage> ApproveAsync(AuthResponse superAdmin, long id, bool asOvertime)
        => Client.PostJsonAsync($"/api/blocked-changes/{id}/force", new { asOvertime }, superAdmin.AccessToken);

    [Fact]
    public async Task While_off_they_log_change_and_remove_hours_in_a_reopened_past_month()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        await _steps.SetJoinedOnAsync(employee.User.Id, "2024-01-01");
        try
        {
            (await Client.PutJsonAsync($"/api/reopened-months/{ReopenedMonth}",
                new { openUntil = Today.AddDays(7).ToString("yyyy-MM-dd"), note = "Catching up on hours" }, superAdmin.AccessToken))
                .IsSuccessStatusCode.ShouldBeTrue();

            var logged = await Work.LogAsync(employee.AccessToken, projectId, InJuly(), 120);
            var edited = await OkAsync<WorkEntryResponse>(Client.PutJsonAsync($"/api/work-entries/{logged.Id}",
                new { projectId, description = "Facade details", startedAt = InJuly(), endedAt = InJuly(9) }, employee.AccessToken));
            edited.Minutes.ShouldBe(180);
            (await Client.DeleteAsync($"/api/work-entries/{logged.Id}", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            (await OkAsync<WorkEntryResponse>(Client.PostEmptyAsync($"/api/work-entries/{logged.Id}/restore", employee.AccessToken))).Id.ShouldBe(logged.Id);

            // the super admins are no longer alerted about it: nothing is wrong
            (await OfKindAsync(superAdmin, NotificationKinds.TimeOffHours)).ShouldNotContain(n => n.Actor != null && n.Actor.Id == employee.User.Id);
        }
        finally
        {
            await Client.DeleteAsync($"/api/reopened-months/{ReopenedMonth}", superAdmin.AccessToken);
        }
    }

    [Fact]
    public async Task Hours_on_a_day_off_wait_for_a_super_admin_and_only_they_decide()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);

        var id = await HeldAsync(employee, projectId);
        (await EntriesAsync(employee.User.Id)).ShouldBe(0);

        var asked = (await OfKindAsync(superAdmin, NotificationKinds.TimeOffHours)).Single(n => n.SubjectId == id);
        asked.SubjectType.ShouldBe("blocked_change");
        (await OfKindAsync(money, NotificationKinds.TimeOffHours)).ShouldBeEmpty();

        var held = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", superAdmin.AccessToken));
        held.ShouldSatisfyAllConditions(
            h => h.Kind.ShouldBe(BlockedChangeKind.WorkEntryTimeOff),
            h => h.Status.ShouldBe(BlockedChangeStatus.Pending),
            h => h.Summary.ShouldStartWith("Hours logged on a day off: 0h 30m on "),
            h => h.RequestedBy.Id.ShouldBe(employee.User.Id));

        // their own day shows them waiting, holding their time
        var day = await OkAsync<MyDayResponse>(Client.GetAsync($"/api/me/day?date={WorkEntryTestSteps.CompanyDay(WorkEntryTestSteps.HoursAgoOnOneDay(3, 30)):yyyy-MM-dd}", employee.AccessToken));
        day.Waiting.ShouldHaveSingleItem().DayOff.ShouldBeTrue();

        // seeing the project's money isn't enough: time off is the super admins'
        ((int)(await Client.GetAsync($"/api/blocked-changes/{id}", money.AccessToken)).StatusCode).ShouldBe(404);
        ((int)(await ApproveAsync(money, id, asOvertime: true)).StatusCode).ShouldBe(404);
        ((int)(await ApproveAsync(employee, id, asOvertime: true)).StatusCode).ShouldBe(403);

        // approving says how they count
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await EntriesAsync(employee.User.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Approved_as_overtime_every_minute_is_overtime_and_the_employee_is_told()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        var id = await HeldAsync(employee, projectId);

        var approved = await OkAsync<BlockedChangeResponse>(ApproveAsync(superAdmin, id, asOvertime: true));
        approved.Status.ShouldBe(BlockedChangeStatus.Forced);

        (await CountAsync("SELECT COUNT(*) FROM work_entries WHERE user_id = @u AND time_off_overtime = 1", ("@u", employee.User.Id))).ShouldBe(1);
        var entryId = await CountAsync("SELECT result_id FROM blocked_changes WHERE id = @id", ("@id", id));
        var entry = (await OkAsync<WorkEntryDetailResponse>(Client.GetAsync($"/api/work-entries/{entryId}", employee.AccessToken))).Entry;
        entry.OvertimeMinutes.ShouldBe(30);

        var told = (await OfKindAsync(employee, NotificationKinds.TimeOffHoursDecided)).ShouldHaveSingleItem();
        told.Payload!.Value.GetProperty("approved").GetBoolean().ShouldBeTrue();
        told.Payload!.Value.GetProperty("asOvertime").GetBoolean().ShouldBeTrue();

        // decided once
        await (await ApproveAsync(superAdmin, id, asOvertime: false)).ShouldFailWithAsync(422, ErrorCodes.BlockedChangeDecided);
    }

    [Fact]
    public async Task Approved_as_normal_hours_they_count_as_logged_and_change_freely_afterwards()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        var id = await HeldAsync(employee, projectId);

        await OkAsync<BlockedChangeResponse>(ApproveAsync(superAdmin, id, asOvertime: false));
        var entryId = await CountAsync("SELECT result_id FROM blocked_changes WHERE id = @id", ("@id", id));
        (await CountAsync("SELECT time_off_overtime FROM work_entries WHERE id = @id", ("@id", entryId))).ShouldBe(0);
        var told = (await OfKindAsync(employee, NotificationKinds.TimeOffHoursDecided)).ShouldHaveSingleItem();
        told.Payload!.Value.GetProperty("asOvertime").GetBoolean().ShouldBeFalse();

        // already approved on its day off: edited and deleted with no second approval
        var start = WorkEntryTestSteps.HoursAgoOnOneDay(3, 30);
        var edited = await OkAsync<WorkEntryResponse>(Client.PutJsonAsync($"/api/work-entries/{entryId}",
            new { projectId, description = "Revised layout", startedAt = start, endedAt = start.AddMinutes(20) }, employee.AccessToken));
        edited.Minutes.ShouldBe(20);
        (await Client.DeleteAsync($"/api/work-entries/{entryId}", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await CountAsync("SELECT COUNT(*) FROM blocked_changes WHERE requested_by_id = @u", ("@u", employee.User.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Turned_down_nothing_is_logged_and_the_employee_is_told()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        var id = await HeldAsync(employee, projectId);

        (await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/dismiss", superAdmin.AccessToken)))
            .Status.ShouldBe(BlockedChangeStatus.Dismissed);

        (await EntriesAsync(employee.User.Id)).ShouldBe(0);
        var told = (await OfKindAsync(employee, NotificationKinds.TimeOffHoursDecided)).ShouldHaveSingleItem();
        told.Payload!.Value.GetProperty("approved").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Moving_hours_from_another_day_onto_a_day_off_waits_for_approval()
    {
        var (superAdmin, employee, projectId) = await OffTodayAsync();
        await _steps.SetJoinedOnAsync(employee.User.Id, "2024-01-01");
        try
        {
            (await Client.PutJsonAsync($"/api/reopened-months/{ReopenedMonth}",
                new { openUntil = Today.AddDays(7).ToString("yyyy-MM-dd"), note = "Catching up on hours" }, superAdmin.AccessToken))
                .IsSuccessStatusCode.ShouldBeTrue();
            var logged = await Work.LogAsync(employee.AccessToken, projectId, InJuly(), 30);

            var start = WorkEntryTestSteps.HoursAgoOnOneDay(3, 30);
            var id = (await (await Client.PutJsonAsync($"/api/work-entries/{logged.Id}",
                    new { projectId, description = "Layout work", startedAt = start, endedAt = start.AddMinutes(30) }, employee.AccessToken))
                .ShouldFailWithAsync(422, ErrorCodes.TimeOffHoursHeld)).BlockedChangeId.ShouldNotBeNull();
            (await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", superAdmin.AccessToken)))
                .Summary.ShouldStartWith("Hours moved onto a day off");

            // approved, the entry moves there, counted as overtime
            await OkAsync<BlockedChangeResponse>(ApproveAsync(superAdmin, id, asOvertime: true));
            var moved = (await OkAsync<WorkEntryDetailResponse>(Client.GetAsync($"/api/work-entries/{logged.Id}", employee.AccessToken))).Entry;
            WorkEntryTestSteps.CompanyDay(moved.StartedAt).ShouldBe(WorkEntryTestSteps.CompanyDay(start));
            moved.OvertimeMinutes.ShouldBe(30);
        }
        finally
        {
            await Client.DeleteAsync($"/api/reopened-months/{ReopenedMonth}", superAdmin.AccessToken);
        }
    }
}
