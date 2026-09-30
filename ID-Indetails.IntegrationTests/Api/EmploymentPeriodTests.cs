using System.Globalization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Employees;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Leaving on a last working day, and coming back after time away.</summary>
public class EmploymentPeriodTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut));

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Leaving_takes_a_last_working_day_that_leaves_closed_months_alone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await EmployeeAsync(superAdmin);
        var url = $"/api/employees/{person.User.Id}/deactivate";

        await (await Client.PostJsonAsync(url, new { lastWorkingDay = Iso(Today.AddDays(1)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.LastWorkingDayInvalid);
        await (await Client.PostJsonAsync(url, new { lastWorkingDay = "2025-12-31" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.LastWorkingDayInvalid);
        var twoMonthsBack = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        await (await Client.PostJsonAsync(url, new { lastWorkingDay = Iso(twoMonthsBack) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);

        // Yesterday always leaves closed months as they were.
        var response = await Client.PostJsonAsync(url, new { lastWorkingDay = Iso(Today.AddDays(-1)) }, superAdmin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        (await response.ReadAsync<EmployeeResponse>()).LeftOn.ShouldBe(Today.AddDays(-1));
    }

    [Fact]
    public async Task Time_away_from_an_earlier_month_is_never_paid_and_starts_a_new_period()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await EmployeeAsync(superAdmin);
        await LeftOnAsync(superAdmin, person, new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2));
        var url = $"/api/employees/{person.User.Id}/reactivate";

        await (await Client.PostJsonAsync(url, new { payGap = true }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PayGapNotAsked);
        (await Client.PostEmptyAsync(url, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM employment_periods WHERE user_id = @id", ("@id", person.User.Id))).ShouldBe(2);
        (await _steps.ScalarAsync<DateTime>(
                "SELECT started_on FROM employment_periods WHERE user_id = @id AND ended_on IS NULL", ("@id", person.User.Id)))
            .ShouldBe(Today.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public async Task Time_away_that_began_this_month_is_paid_only_if_the_admin_says_so()
    {
        // The gap needs a day between leaving on the 1st and coming back today.
        if (Today.Day < 3)
        {
            return;
        }

        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var first = new DateOnly(Today.Year, Today.Month, 1);

        var paid = await EmployeeAsync(superAdmin);
        await LeftOnAsync(superAdmin, paid, first);
        var paidUrl = $"/api/employees/{paid.User.Id}/reactivate";
        await (await Client.PostEmptyAsync(paidUrl, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PayGapRequired);
        (await Client.PostJsonAsync(paidUrl, new { payGap = true }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // One period that simply goes on, with the days away excused and paid.
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM employment_periods WHERE user_id = @id AND ended_on IS NULL", ("@id", paid.User.Id)))
            .ShouldBe(1);
        (await _steps.ScalarAsync<int>(
                "SELECT COUNT(*) FROM hour_excuse_days d JOIN hour_excuses x ON x.id = d.excuse_id WHERE x.user_id = @id AND x.is_paid",
                ("@id", paid.User.Id)))
            .ShouldBe(Today.Day - 2);

        var unpaid = await EmployeeAsync(superAdmin);
        await LeftOnAsync(superAdmin, unpaid, first);
        (await Client.PostJsonAsync($"/api/employees/{unpaid.User.Id}/reactivate", new { payGap = false }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM employment_periods WHERE user_id = @id", ("@id", unpaid.User.Id))).ShouldBe(2);
    }

    /// <summary>A Design employee who joined on 1 January 2026.</summary>
    private async Task<AuthResponse> EmployeeAsync(AuthResponse superAdmin)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        return person;
    }

    /// <summary>Deactivates, then moves the last working day back to <paramref name="day"/>.</summary>
    private async Task LeftOnAsync(AuthResponse superAdmin, AuthResponse person, DateOnly day)
    {
        (await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await _steps.SetLeftAtAsync(person.User.Id, $"{Iso(day)} 12:00:00");
    }
}
