using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: settings and the working week are company-wide.</summary>
public class CompanyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Super_admins_set_the_company_settings_everyone_reads()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var body = new
        {
            name = "  Indetails  ",
            address = " Hamra Street, Beirut, Lebanon ",
            website = "indetails.com",
            weekStart = 0,
            use24HourTime = true,
            defaultCurrency = "lbp",
        };

        // before anything is saved, the company goes by its default name
        var fresh = await OkAsync<CompanySettingsResponse>(Client.GetAsync("/api/company/settings", admin.AccessToken));
        fresh.IsSetUp.ShouldBeFalse();
        fresh.Name.ShouldBe("ID InDetails");

        await (await Client.PutJsonAsync("/api/company/settings", body, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var saved = await OkAsync<CompanySettingsResponse>(Client.PutJsonAsync("/api/company/settings", body, superAdmin.AccessToken));
        saved.IsSetUp.ShouldBeTrue();
        saved.Name.ShouldBe("Indetails");
        saved.Address.ShouldBe("Hamra Street, Beirut, Lebanon");
        saved.Website.ShouldBe("indetails.com");
        saved.DefaultCurrency.ShouldBe("LBP");
        saved.TimeZone.ShouldBe("Asia/Beirut");

        (await OkAsync<CompanySettingsResponse>(Client.GetAsync("/api/company/settings", admin.AccessToken))).WeekStart.ShouldBe((byte)0);

        await (await Client.PutJsonAsync("/api/company/settings", new { name = "", weekStart = 3, defaultCurrency = "EUR" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync("/api/company/settings", new { name = "Indetails", website = "not a site" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task The_working_week_changes_from_today_or_later_and_a_future_change_can_be_replaced()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var later = Today.AddDays(40);
        object Week(DateOnly from, int end) => new
        {
            effectiveFrom = Iso(from), workDays = new[] { 1, 2, 3, 4, 5 }, startMinute = 480, endMinute = end, breakMinutes = 60, overtimeMultiplier = 1.5m,
        };

        await (await Client.PostJsonAsync("/api/company/working-time", Week(Today.AddDays(-1), 960), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.WorkingTimeInPast);
        await (await Client.PostJsonAsync("/api/company/working-time", Week(later, 960), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var set = await OkAsync<WorkingTimeResponse>(Client.PostJsonAsync("/api/company/working-time", Week(later, 960), superAdmin.AccessToken));
        set.Current.EffectiveFrom.ShouldBeLessThan(later);
        set.Policies.First().EffectiveFrom.ShouldBe(later);
        set.Policies.First().RequiredMinutesPerDay.ShouldBe(420);

        var replaced = await OkAsync<WorkingTimeResponse>(Client.PostJsonAsync("/api/company/working-time", Week(later, 1020), superAdmin.AccessToken));
        replaced.Policies.Count.ShouldBe(set.Policies.Count);
        replaced.Policies.First().RequiredMinutesPerWeek.ShouldBe(5 * 480);

        (await OkAsync<WorkingTimeResponse>(Client.GetAsync("/api/company/working-time", admin.AccessToken))).Policies.Count.ShouldBe(set.Policies.Count);
    }

    [Fact]
    public async Task A_personal_schedule_replaces_the_company_week_for_that_person()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (email, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{designer.User.Id}/schedule";

        // Monday to Thursday, 8 to 4 (7h required each day after the paid break).
        var ownWeek = new { days = Enumerable.Range(1, 4).Select(d => new { weekday = d, startMinute = 480, endMinute = 960 }) };
        var own = await OkAsync<PersonScheduleResponse>(Client.PutJsonAsync(url, ownWeek, superAdmin.AccessToken));
        own.IsOwn.ShouldBeTrue();
        own.OwnSince.ShouldBe(Today);
        own.ChangedOn.ShouldBe(Today);
        own.RequiredMinutesPerWeek.ShouldBe(4 * 420);

        await (await Client.PutJsonAsync(url, new { followsDefault = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WorkingTimeAlreadyChanged);

        var planned = await OkAsync<PersonScheduleResponse>(Client.PutJsonAsync(url,
            new { followsDefault = true, effectiveFrom = Iso(Today.AddDays(7)) }, superAdmin.AccessToken));
        planned.IsOwn.ShouldBeTrue();
        planned.NextChangeOn.ShouldBe(Today.AddDays(7));
        planned.ChangedOn.ShouldBe(Today);

        var list = await OkAsync<SchedulesResponse>(Client.GetAsync($"/api/company/schedules?search={designer.User.LastName}", superAdmin.AccessToken));
        list.People.ShouldContain(p => p.UserId == designer.User.Id && p.IsOwn);

        await (await Client.PutJsonAsync(url, ownWeek, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync(url, new { days = new[] { new { weekday = 9, startMinute = 600, endMinute = 610 } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        email.ShouldNotBeNull();
    }

    [Fact]
    public async Task Schedules_download_as_a_pdf_and_a_csv_with_a_row_per_person()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // Monday to Thursday, 8 to 4: 28 hours a week.
        var ownWeek = new { days = Enumerable.Range(1, 4).Select(d => new { weekday = d, startMinute = 480, endMinute = 960 }) };
        await OkAsync<PersonScheduleResponse>(Client.PutJsonAsync($"/api/employees/{designer.User.Id}/schedule", ownWeek, superAdmin.AccessToken));
        var query = $"?domain=design&search={designer.User.LastName}";

        var pdf = await Client.GetAsync($"/api/company/schedules/pdf{query}", superAdmin.AccessToken);
        ((int)pdf.StatusCode).ShouldBe(200, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"schedules-{Iso(Today)}.pdf");
        System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(), 0, 5).ShouldBe("%PDF-");

        var export = await Client.GetAsync($"/api/company/schedules/export{query}", superAdmin.AccessToken);
        ((int)export.StatusCode).ShouldBe(200, await export.Content.ReadAsStringAsync());
        export.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        export.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"schedules-{Iso(Today)}.csv");
        var csv = await export.Content.ReadAsStringAsync();
        csv.ShouldContain("Name,Business,Department,Position,Schedule,");
        csv.ShouldContain(",Hours a week,Next change");
        var row = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Single(l => l.Contains($"{designer.User.FirstName} {designer.User.LastName}"));
        row.ShouldContain(",Design,");
        row.ShouldContain(",Own,");
        row.Split("08:00-16:00").Length.ShouldBe(5);
        row.ShouldEndWith(",28,");

        foreach (var path in new[] { "pdf", "export" })
        {
            await (await Client.GetAsync($"/api/company/schedules/{path}", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }
    }
    [Fact]
    public async Task Company_expenses_keep_past_months_and_totals_per_currency()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var token = superAdmin.AccessToken;

        var design = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Design tools", groupId = 1, currency = "USD", amount = 100m }, token));
        await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Generator", groupId = 2, currency = "LBP", amount = 895_000m }, token));

        // A raise from next month leaves this month at 100.
        var raised = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync($"/api/company/overhead/{design.Id}/amounts",
            new { amount = 120m, fromMonth = Iso(thisMonth.AddMonths(1)) }, token));
        raised.CurrentAmount.ShouldBe(100m);
        raised.Amounts.Count.ShouldBe(2);

        await (await Client.PostJsonAsync($"/api/company/overhead/{design.Id}/amounts",
            new { amount = 90m, fromMonth = Iso(thisMonth.AddMonths(-1)) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OverheadMonthInPast);

        var overview = await OkAsync<OverheadResponse>(Client.GetAsync("/api/company/overhead", token));
        overview.MonthTotals.ShouldContain(t => t.Currency == "USD" && t.Total >= 100m);
        overview.MonthTotals.ShouldContain(t => t.Currency == "LBP" && t.Total >= 895_000m);

        var months = await OkAsync<List<OverheadMonthResponse>>(Client.GetAsync("/api/company/overhead/monthly?months=13", token));
        months.Count.ShouldBe(13);
        months[^1].Month.ShouldBe(thisMonth);

        var cancelled = await OkAsync<OverheadLineResponse>(Client.PostEmptyAsync($"/api/company/overhead/{design.Id}/cancel", token));
        cancelled.EndedOn.ShouldBe(thisMonth.AddMonths(1).AddDays(-1));
        cancelled.CurrentAmount.ShouldBe(100m);
        (await OkAsync<OverheadLineResponse>(Client.PostEmptyAsync($"/api/company/overhead/{design.Id}/resume", token))).EndedOn.ShouldBeNull();

        // A line that hasn't started yet is simply removed when cancelled.
        var planned = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Next year", groupId = 1, currency = "USD", amount = 10m, startMonth = Iso(thisMonth.AddMonths(2)) }, token));
        (await Client.PostEmptyAsync($"/api/company/overhead/{planned.Id}/cancel", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        await (await Client.GetAsync("/api/company/overhead", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        design.Group.ShouldBe(new OverheadGroupSummaryResponse(1, "Software & services"));
    }

    [Fact]
    public async Task Super_admins_manage_the_categories_and_one_in_use_stays()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var token = superAdmin.AccessToken;

        // seeded, first to last, "Other" at the end
        var seeded = await OkAsync<List<OverheadGroupResponse>>(Client.GetAsync("/api/company/overhead/groups", token));
        seeded[0].Name.ShouldBe("Software & services");
        seeded[1].Name.ShouldBe("Operating costs");
        seeded[^1].Name.ShouldBe("Other");
        seeded.Count.ShouldBe(14);

        // a new one goes before "Other", and names are unique whatever their case
        var studio = await OkAsync<OverheadGroupResponse>(Client.PostJsonAsync("/api/company/overhead/groups", new { name = "  Studio costs " }, token));
        studio.Name.ShouldBe("Studio costs");
        var listed = await OkAsync<List<OverheadGroupResponse>>(Client.GetAsync("/api/company/overhead/groups", token));
        listed[^2].Id.ShouldBe(studio.Id);
        listed[^1].Name.ShouldBe("Other");
        await (await Client.PostJsonAsync("/api/company/overhead/groups", new { name = "studio COSTS" }, token))
            .ShouldFailWithAsync(409, ErrorCodes.OverheadGroupExists);
        await (await Client.PostJsonAsync("/api/company/overhead/groups", new { name = " " }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // a line put in it, and moved by renaming nothing
        var line = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Studio cleaning", groupId = studio.Id, currency = "USD", amount = 150m }, token));
        line.Group.ShouldBe(new OverheadGroupSummaryResponse(studio.Id, "Studio costs"));
        (await OkAsync<OverheadGroupResponse>(Client.PutJsonAsync($"/api/company/overhead/groups/{studio.Id}", new { name = "Studio upkeep" }, token)))
            .Lines.ShouldBe(1);
        var totals = (await OkAsync<OverheadResponse>(Client.GetAsync("/api/company/overhead", token))).GroupTotals;
        totals.ShouldContain(t => t.GroupId == studio.Id && t.Name == "Studio upkeep" && t.Currency == "USD" && t.Total == 150m);

        // in use, it stays; once its line moves, it can go
        await (await Client.DeleteAsync($"/api/company/overhead/groups/{studio.Id}", token)).ShouldFailWithAsync(422, ErrorCodes.OverheadGroupInUse);
        (await OkAsync<OverheadLineResponse>(Client.PutJsonAsync($"/api/company/overhead/{line.Id}", new { name = "Studio cleaning", groupId = 2 }, token)))
            .Group.Name.ShouldBe("Operating costs");
        (await Client.DeleteAsync($"/api/company/overhead/groups/{studio.Id}", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.PostJsonAsync("/api/company/overhead",
                new { name = "Gone", groupId = studio.Id, currency = "USD", amount = 1m }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OverheadGroupNotFound);

        // overhead:manage doesn't manage them, overhead_category:manage does; anyone who sees the expenses reads them
        var manager = (await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design)).Auth;
        var granted = await Client.PutJsonAsync($"/api/users/{manager.User.Id}/permissions", new
        {
            changes = new[] { new { code = PermissionCodes.OverheadManage, action = PermissionChangeAction.Grant, reason = "Covering expenses" } },
        }, token);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        (await Client.GetAsync("/api/company/overhead/groups", manager.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        await (await Client.PostJsonAsync("/api/company/overhead/groups", new { name = "Snacks" }, manager.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/company/overhead/groups", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var categories = await Client.PutJsonAsync($"/api/users/{manager.User.Id}/permissions", new
        {
            changes = new[] { new { code = PermissionCodes.OverheadCategoryManage, action = PermissionChangeAction.Grant, reason = "Tidies the categories" } },
        }, token);
        categories.IsSuccessStatusCode.ShouldBeTrue(await categories.Content.ReadAsStringAsync());
        var snacks = await OkAsync<OverheadGroupResponse>(Client.PostJsonAsync("/api/company/overhead/groups", new { name = "Snacks" }, manager.AccessToken));
        (await Client.DeleteAsync($"/api/company/overhead/groups/{snacks.Id}", manager.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Changing_an_expense_s_currency_keeps_past_months_in_the_old_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var token = superAdmin.AccessToken;
        var rent = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Office rent", groupId = 2, currency = "LBP", amount = 71_600_000m }, token));

        // Charged this month in LBP, so from next month a new USD line takes over and this one ends with the month.
        var usd = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync($"/api/company/overhead/{rent.Id}/currency",
            new { currency = "USD", amount = 800m, fromMonth = Iso(thisMonth.AddMonths(1)) }, token));
        usd.Id.ShouldNotBe(rent.Id);
        usd.Name.ShouldBe("Office rent");
        usd.Group.Id.ShouldBe(2);
        usd.Currency.ShouldBe("USD");
        usd.StartedOn.ShouldBe(thisMonth.AddMonths(1));
        var lines = (await OkAsync<OverheadResponse>(Client.GetAsync("/api/company/overhead", token))).Lines;
        lines.Single(l => l.Id == rent.Id).EndedOn.ShouldBe(thisMonth.AddMonths(1).AddDays(-1));
        lines.Single(l => l.Id == rent.Id).CurrentAmount.ShouldBe(71_600_000m);
        // the USD line carries it on, so resuming the LBP one would charge both
        await (await Client.PostEmptyAsync($"/api/company/overhead/{rent.Id}/resume", token)).ShouldFailWithAsync(422, ErrorCodes.OverheadCarriedOn);

        // A line not charged before the change just changes.
        var planned = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Hosting", groupId = 1, currency = "LBP", amount = 900_000m, startMonth = Iso(thisMonth.AddMonths(2)) }, token));
        var switched = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync($"/api/company/overhead/{planned.Id}/currency",
            new { currency = "USD", amount = 10m }, token));
        switched.Id.ShouldBe(planned.Id);
        switched.Currency.ShouldBe("USD");
        switched.Amounts.ShouldHaveSingleItem().Amount.ShouldBe(10m);

        await (await Client.PostJsonAsync($"/api/company/overhead/{usd.Id}/currency", new { currency = "USD", amount = 5m }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OverheadSameCurrency);
        await (await Client.PostJsonAsync($"/api/company/overhead/{rent.Id}/currency", new { currency = "USD", amount = 5m }, token))
            .ShouldFailWithAsync(422, ErrorCodes.OverheadAlreadyCancelled);
        await (await Client.PostJsonAsync($"/api/company/overhead/{usd.Id}/currency", new { currency = "LBP", amount = 5m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task One_off_expenses_are_dated_this_month_and_a_closed_month_is_locked()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var token = superAdmin.AccessToken;
        const string url = "/api/company/overhead/one-offs";

        var fridge = await OkAsync<OneOffExpenseResponse>(Client.PostJsonAsync(url,
            new { name = "  Refrigerator ", groupId = 1, currency = "usd", amount = 450m, spentOn = Iso(Today) }, token));
        fridge.Name.ShouldBe("Refrigerator");
        fridge.Currency.ShouldBe("USD");
        fridge.Group.ShouldBe(new OverheadGroupSummaryResponse(1, "Software & services"));
        fridge.SpentOn.ShouldBe(Today);
        fridge.CanEdit.ShouldBeTrue();

        var list = await OkAsync<OneOffExpenseListResponse>(Client.GetAsync(url, token));
        list.Month.ShouldBe(thisMonth);
        list.IsOpen.ShouldBeTrue();
        list.Items.ShouldContain(i => i.Id == fridge.Id && i.CanEdit);
        list.Totals.ShouldContain(t => t.Currency == "USD" && t.Total >= 450m);
        (await OkAsync<List<OverheadMonthResponse>>(Client.GetAsync("/api/company/overhead/monthly?months=2", token)))[^1]
            .OneOffTotals.ShouldContain(t => t.Currency == "USD" && t.Total >= 450m);

        // changed in place, to the first of the month
        var fixedUp = await OkAsync<OneOffExpenseResponse>(Client.PutJsonAsync($"{url}/{fridge.Id}",
            new { name = "Fridge", groupId = 2, currency = "USD", amount = 500m, spentOn = Iso(thisMonth) }, token));
        fixedUp.Name.ShouldBe("Fridge");
        fixedUp.Group.Id.ShouldBe(2);
        fixedUp.Amount.ShouldBe(500m);
        fixedUp.SpentOn.ShouldBe(thisMonth);

        // the day is this month, up to today
        await (await Client.PostJsonAsync(url, new { name = "Late", groupId = 1, currency = "USD", amount = 1m, spentOn = Iso(Today.AddDays(1)) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OneOffOutsideThisMonth);
        await (await Client.PutJsonAsync($"{url}/{fridge.Id}",
                new { name = "Fridge", groupId = 2, currency = "USD", amount = 500m, spentOn = Iso(thisMonth.AddDays(-1)) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OneOffOutsideThisMonth);
        await (await Client.PostJsonAsync(url, new { name = "Nowhere", groupId = 9999, currency = "USD", amount = 1m, spentOn = Iso(Today) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.OverheadGroupNotFound);
        await (await Client.PostJsonAsync(url, new { name = " ", groupId = 1, currency = "EUR", amount = 0m, spentOn = Iso(Today) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"{url}/9999999", new { name = "Nothing", groupId = 1, currency = "USD", amount = 1m, spentOn = Iso(Today) }, token))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a category a one-off uses stays, and counts it
        var groups = await OkAsync<List<OverheadGroupResponse>>(Client.GetAsync("/api/company/overhead/groups", token));
        groups.Single(g => g.Id == 2).Lines.ShouldBeGreaterThanOrEqualTo(1);
        var snacks = await OkAsync<OverheadGroupResponse>(Client.PostJsonAsync("/api/company/overhead/groups", new { name = "Pantry" }, token));
        var biscuits = await OkAsync<OneOffExpenseResponse>(Client.PostJsonAsync(url,
            new { name = "Biscuits", groupId = snacks.Id, currency = "LBP", amount = 900_000m, spentOn = Iso(Today) }, token));
        await (await Client.DeleteAsync($"/api/company/overhead/groups/{snacks.Id}", token)).ShouldFailWithAsync(422, ErrorCodes.OverheadGroupInUse);

        // a past month is read-only, whatever the caller holds
        await ExecuteAsync(
            "INSERT INTO one_off_expenses (name, group_id, currency, amount, spent_on, created_at) " +
            "VALUES ('Old repair', 1, 'USD', 80, @day, UTC_TIMESTAMP(3))", ("@day", thisMonth.AddMonths(-1).AddDays(14).ToDateTime(TimeOnly.MinValue)));
        var previous = await OkAsync<OneOffExpenseListResponse>(Client.GetAsync($"{url}?month={thisMonth.AddMonths(-1):yyyy-MM}", token));
        previous.IsOpen.ShouldBeFalse();
        var old = previous.Items.Single(i => i.Name == "Old repair");
        old.CanEdit.ShouldBeFalse();
        await (await Client.PutJsonAsync($"{url}/{old.Id}",
                new { name = "Old repair", groupId = 1, currency = "USD", amount = 90m, spentOn = Iso(Today) }, token))
            .ShouldFailWithAsync(422, ErrorCodes.OneOffLocked);
        await (await Client.DeleteAsync($"{url}/{old.Id}", token)).ShouldFailWithAsync(422, ErrorCodes.OneOffLocked);
        await (await Client.GetAsync($"{url}?month={thisMonth.AddMonths(1):yyyy-MM}", token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"{url}?month=soon", token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // only overhead:manage changes them; whoever sees the company's expenses reads them
        await (await Client.GetAsync(url, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync(url, new { name = "Chair", groupId = 1, currency = "USD", amount = 60m, spentOn = Iso(Today) }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"{url}/{fridge.Id}", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await Client.DeleteAsync($"{url}/{biscuits.Id}", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{url}/{fridge.Id}", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await OkAsync<OneOffExpenseListResponse>(Client.GetAsync(url, token))).Items.ShouldNotContain(i => i.Id == fridge.Id);
        await (await Client.DeleteAsync($"{url}/{fridge.Id}", token)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // each change is a company-level event with the amount, in the Company group
        var feed = await OkAsync<PagedResponse<ActivityItemResponse>>(Client.GetAsync("/api/activity?group=company&pageSize=50", token));
        var mine = feed.Items.Where(i => i.SubjectType == "one_off_expense" && i.SubjectId == fridge.Id).ToList();
        mine.Select(i => i.Kind).ShouldBe(
            [ActivityEventKinds.OverheadOneOffRemoved, ActivityEventKinds.OverheadOneOffChanged, ActivityEventKinds.OverheadOneOffAdded],
            ignoreOrder: true);
        mine.ShouldAllBe(i => i.Group == ActivityGroup.Company && i.Domain == null);
        mine.Single(i => i.Kind == ActivityEventKinds.OverheadOneOffChanged).Amount.ShouldBe(500m);
        mine.Single(i => i.Kind == ActivityEventKinds.OverheadOneOffChanged).PrevAmount.ShouldBe(450m);
    }
}
