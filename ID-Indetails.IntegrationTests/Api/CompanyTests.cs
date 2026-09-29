using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;
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
        own.RequiredMinutesPerWeek.ShouldBe(4 * 420);

        await (await Client.PutJsonAsync(url, new { followsDefault = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WorkingTimeAlreadyChanged);

        var planned = await OkAsync<PersonScheduleResponse>(Client.PutJsonAsync(url,
            new { followsDefault = true, effectiveFrom = Iso(Today.AddDays(7)) }, superAdmin.AccessToken));
        planned.IsOwn.ShouldBeTrue();
        planned.NextChangeOn.ShouldBe(Today.AddDays(7));

        var list = await OkAsync<SchedulesResponse>(Client.GetAsync($"/api/company/schedules?search={designer.User.LastName}", superAdmin.AccessToken));
        list.People.ShouldContain(p => p.UserId == designer.User.Id && p.IsOwn);

        await (await Client.PutJsonAsync(url, ownWeek, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync(url, new { days = new[] { new { weekday = 9, startMinute = 600, endMinute = 610 } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        email.ShouldNotBeNull();
    }

    [Fact]
    public async Task Company_expenses_keep_past_months_and_totals_per_currency()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var token = superAdmin.AccessToken;

        var design = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Design tools", costGroup = "software", currency = "USD", amount = 100m }, token));
        await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Generator", costGroup = "operating", currency = "LBP", amount = 895_000m }, token));

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
            new { name = "Next year", costGroup = "software", currency = "USD", amount = 10m, startMonth = Iso(thisMonth.AddMonths(2)) }, token));
        (await Client.PostEmptyAsync($"/api/company/overhead/{planned.Id}/cancel", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        await (await Client.GetAsync("/api/company/overhead", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        design.CostGroup.ShouldBe(OverheadCostGroup.Software);
    }

    [Fact]
    public async Task Changing_an_expense_s_currency_keeps_past_months_in_the_old_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var token = superAdmin.AccessToken;
        var rent = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Office rent", costGroup = "operating", currency = "LBP", amount = 71_600_000m }, token));

        // Charged this month in LBP, so from next month a new USD line takes over and this one ends with the month.
        var usd = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync($"/api/company/overhead/{rent.Id}/currency",
            new { currency = "USD", amount = 800m, fromMonth = Iso(thisMonth.AddMonths(1)) }, token));
        usd.Id.ShouldNotBe(rent.Id);
        usd.Name.ShouldBe("Office rent");
        usd.CostGroup.ShouldBe(OverheadCostGroup.Operating);
        usd.Currency.ShouldBe("USD");
        usd.StartedOn.ShouldBe(thisMonth.AddMonths(1));
        var lines = (await OkAsync<OverheadResponse>(Client.GetAsync("/api/company/overhead", token))).Lines;
        lines.Single(l => l.Id == rent.Id).EndedOn.ShouldBe(thisMonth.AddMonths(1).AddDays(-1));
        lines.Single(l => l.Id == rent.Id).CurrentAmount.ShouldBe(71_600_000m);

        // A line not charged before the change just changes.
        var planned = await OkAsync<OverheadLineResponse>(Client.PostJsonAsync("/api/company/overhead",
            new { name = "Hosting", costGroup = "software", currency = "LBP", amount = 900_000m, startMonth = Iso(thisMonth.AddMonths(2)) }, token));
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
}
