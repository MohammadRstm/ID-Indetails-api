using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Supervision taken: the company withdrawing some of a site's supervision fee. Not a cost, so profit never moves.</summary>
public class SupervisionWithdrawalTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>The company's day, as the API counts it.</summary>
    private static DateOnly CompanyToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut")));

    private static string Day(int daysAgo) => CompanyToday().AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private async Task<SupervisionWithdrawalResponse> TakeAsync(long projectId, string accessToken, decimal amountUsd, string takenOn, string? note = null)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/supervision-withdrawals", new { amountUsd, takenOn, note }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SupervisionWithdrawalResponse>();
    }

    private async Task<IReadOnlyList<SupervisionWithdrawalResponse>> ListAsync(long projectId, string accessToken)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/supervision-withdrawals", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<SupervisionWithdrawalResponse>>();
    }

    private async Task<ProjectMoneyResponse> MoneyAsync(long projectId, string accessToken)
        => (await (await Client.GetAsync($"/api/projects/{projectId}", accessToken)).ReadAsync<ProjectResponse>()).Money!;

    /// <summary>An admin of Execution given exactly <paramref name="codes"/> beyond what an admin holds.</summary>
    private async Task<AuthResponse> AdminWithAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = codes.Select(code => new { code, action = "grant", reason = "Test" }) }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }

    [Fact]
    public async Task Supervision_taken_is_recorded_listed_latest_day_first_and_removed_without_touching_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var before = await MoneyAsync(site.Id, superAdmin.AccessToken);

        var first = await TakeAsync(site.Id, superAdmin.AccessToken, 250.50m, Day(10), "  First tranche ");
        var second = await TakeAsync(site.Id, superAdmin.AccessToken, 100m, Day(3));
        var third = await TakeAsync(site.Id, superAdmin.AccessToken, 500m, Day(10));

        first.ShouldSatisfyAllConditions(
            w => w.AmountUsd.ShouldBe(250.50m),
            w => w.TakenOn.ToString("yyyy-MM-dd").ShouldBe(Day(10)),
            w => w.Note.ShouldBe("First tranche"),
            w => w.CreatedBy!.Id.ShouldBe(superAdmin.User.Id),
            w => w.CreatedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5)));
        second.Note.ShouldBeNull();

        // the latest day first; the same day, the one recorded last
        (await ListAsync(site.Id, superAdmin.AccessToken)).Select(w => w.Id).ShouldBe([second.Id, third.Id, first.Id]);

        // nothing about the site's profit moved: it is not a cost, and more was taken than this site has due
        var during = await MoneyAsync(site.Id, superAdmin.AccessToken);
        during.ShouldSatisfyAllConditions(
            m => m.Profit.ShouldBe(before.Profit),
            m => m.Cost.Total.ShouldBe(before.Cost.Total),
            m => m.Revenue.ShouldBe(before.Revenue),
            m => m.CashProfit.ShouldBe(before.CashProfit));

        var removed = await Client.DeleteAsync($"/api/supervision-withdrawals/{third.Id}", superAdmin.AccessToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(site.Id, superAdmin.AccessToken)).Select(w => w.Id).ShouldBe([second.Id, first.Id]);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM supervision_withdrawals WHERE id = @w AND deleted_at IS NOT NULL", ("@w", third.Id))).ShouldBe(1);
        (await MoneyAsync(site.Id, superAdmin.AccessToken)).Profit.ShouldBe(before.Profit);

        // removed once
        await (await Client.DeleteAsync($"/api/supervision-withdrawals/{third.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync("/api/supervision-withdrawals/999999999", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'supervision_taken'", ("@p", site.Id))).ShouldBe(3);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'supervision_withdrawal_removed' AND subject_id = @w AND amount = 500",
            ("@p", site.Id), ("@w", third.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task The_amount_the_day_and_the_note_are_checked()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var url = $"/api/projects/{site.Id}/supervision-withdrawals";
        var token = superAdmin.AccessToken;

        foreach (var amountUsd in new[] { 0m, -5m, 10.005m, 1_000_000_000_000m })
        {
            var error = await (await Client.PostJsonAsync(url, new { amountUsd, takenOn = Day(1) }, token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
            error.Errors!.Keys.ShouldContain("amountUsd");
        }

        // a missing amount is 0; a missing or out-of-range day is refused too
        var noDay = await (await Client.PostJsonAsync(url, new { amountUsd = 10m }, token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        noDay.Errors!.Keys.ShouldContain("takenOn");
        await (await Client.PostJsonAsync(url, new { amountUsd = 10m, takenOn = "1999-12-31" }, token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var longNote = await (await Client.PostJsonAsync(url, new { amountUsd = 10m, takenOn = Day(1), note = new string('x', 501) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        longNote.Errors!.Keys.ShouldContain("note");

        // not on a day that hasn't come, in the company's time zone
        var future = await (await Client.PostJsonAsync(url, new { amountUsd = 10m, takenOn = CompanyToday().AddDays(1).ToString("yyyy-MM-dd") }, token))
            .ShouldFailWithAsync(400, ErrorCodes.TakenDateInFuture);
        future.Errors!.Keys.ShouldContain("takenOn");

        (await ListAsync(site.Id, token)).ShouldBeEmpty();

        // today, the largest amount and the longest note are fine
        await TakeAsync(site.Id, token, 10m, CompanyToday().ToString("yyyy-MM-dd"), new string('x', 500));
        await TakeAsync(site.Id, token, 999_999_999_999.99m, Day(1));
        (await ListAsync(site.Id, token)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Recording_and_removing_need_the_permission_to_record_payments_and_reading_needs_to_see_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var recorder = await AdminWithAsync(superAdmin, PermissionCodes.PaymentRecord);
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var existing = await TakeAsync(site.Id, superAdmin.AccessToken, 50m, Day(2));
        var url = $"/api/projects/{site.Id}/supervision-withdrawals";

        // payment:record alone writes, but cannot read the list
        var made = await TakeAsync(site.Id, recorder.AccessToken, 75m, Day(1));
        made.CreatedBy!.Id.ShouldBe(recorder.User.Id);
        await (await Client.GetAsync(url, recorder.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await Client.DeleteAsync($"/api/supervision-withdrawals/{made.Id}", recorder.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // financial:view alone reads, but cannot write
        (await ListAsync(site.Id, viewer.AccessToken)).Select(w => w.Id).ShouldBe([existing.Id]);
        await (await Client.PostJsonAsync(url, new { amountUsd = 5m, takenOn = Day(1) }, viewer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/supervision-withdrawals/{existing.Id}", viewer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // neither can do anything
        await (await Client.GetAsync(url, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync(url, new { amountUsd = 5m, takenOn = Day(1) }, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/supervision-withdrawals/{existing.Id}", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(url)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);

        (await ListAsync(site.Id, superAdmin.AccessToken)).Select(w => w.Id).ShouldBe([existing.Id]);
    }

    [Fact]
    public async Task A_site_of_a_domain_the_caller_cannot_see_reads_as_missing_and_a_design_project_has_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var existing = await TakeAsync(site.Id, superAdmin.AccessToken, 50m, Day(2));
        var urlOf = (long id) => $"/api/projects/{id}/supervision-withdrawals";

        // holding both permissions in Design does not open Execution
        await (await Client.GetAsync(urlOf(site.Id), designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync(urlOf(site.Id), new { amountUsd = 5m, takenOn = Day(1) }, designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/supervision-withdrawals/{existing.Id}", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(urlOf(999_999_999), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a Design project the caller sees has no supervision to take
        await (await Client.GetAsync(urlOf(design.Id), designMoney.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.PostJsonAsync(urlOf(design.Id), new { amountUsd = 5m, takenOn = Day(1) }, designMoney.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);

        (await ListAsync(site.Id, superAdmin.AccessToken)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_finalised_site_is_locked_until_it_is_reopened_but_still_readable()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var kept = await TakeAsync(site.Id, superAdmin.AccessToken, 40m, Day(2));

        (await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/supervision-withdrawals", new { amountUsd = 5m, takenOn = Day(1) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.DeleteAsync($"/api/supervision-withdrawals/{kept.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        (await ListAsync(site.Id, superAdmin.AccessToken)).Select(w => w.Id).ShouldBe([kept.Id]);

        (await Client.PostEmptyAsync($"/api/projects/{site.Id}/reopen", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await TakeAsync(site.Id, superAdmin.AccessToken, 5m, Day(1));
        (await Client.DeleteAsync($"/api/supervision-withdrawals/{kept.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(site.Id, superAdmin.AccessToken)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_activity_feed_shows_them_as_money_events_whose_amount_and_details_only_money_viewers_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ActivityView, action = "grant", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var taken = await TakeAsync(site.Id, superAdmin.AccessToken, 300m, Day(1), "Third quarter");
        (await Client.DeleteAsync($"/api/supervision-withdrawals/{taken.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var money = await (await Client.GetAsync($"/api/activity?group=money&projectId={site.Id}", superAdmin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        var added = money.Items.Single(i => i.Kind == ActivityEventKinds.SupervisionTaken);
        added.ShouldSatisfyAllConditions(
            i => i.Group.ShouldBe(ActivityGroup.Money),
            i => i.Amount.ShouldBe(300m),
            i => i.SubjectType.ShouldBe("supervision_withdrawal"),
            i => i.SubjectId.ShouldBe(taken.Id),
            i => i.Payload!.Value.GetProperty("note").GetString().ShouldBe("Third quarter"),
            i => i.Payload!.Value.GetProperty("takenOn").GetString().ShouldBe(Day(1)));
        var gone = money.Items.Single(i => i.Kind == ActivityEventKinds.SupervisionWithdrawalRemoved);
        gone.Amount.ShouldBe(300m);

        // someone who reads the feed without financial:view sees that it happened, not how much or why
        var limited = await (await Client.GetAsync($"/api/activity?projectId={site.Id}", lead.AccessToken)).ReadAsync<PagedResponse<ActivityItemResponse>>();
        foreach (var kind in new[] { ActivityEventKinds.SupervisionTaken, ActivityEventKinds.SupervisionWithdrawalRemoved })
        {
            var item = limited.Items.Single(i => i.Kind == kind);
            item.Amount.ShouldBeNull();
            item.Payload.ShouldBeNull();
        }
    }
}
