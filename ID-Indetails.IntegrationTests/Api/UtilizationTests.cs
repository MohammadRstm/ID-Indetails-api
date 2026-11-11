using System.Text.Json;
using System.Text.RegularExpressions;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Reports;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Design employees' logged against expected hours in a month: hours only, never pay. Every test follows its own people
/// and projects, since the classes share one database.
/// </summary>
public class UtilizationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static string UtilizationUrl(DateOnly? month = null)
        => "/api/reports/utilization" + (month is { } m ? $"?month={m:yyyy-MM-dd}" : "");

    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private static DateOnly CompanyToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut));

    /// <summary>Every property name anywhere in a JSON value, nested objects and arrays included.</summary>
    private static IEnumerable<string> PropertyNames(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
            _ => [],
        };

    /// <summary>
    /// Up to 45 minutes of work ending a couple of minutes ago, kept within today in Beirut so it is in this month and
    /// on or after a person invited today joined.
    /// </summary>
    private static (string Start, string End, int Minutes) WorkToday()
    {
        var now = DateTime.UtcNow;
        var end = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(-2);
        var midnight = TimeZoneInfo.ConvertTimeToUtc(CompanyToday().ToDateTime(TimeOnly.MinValue), Beirut);
        var start = end.AddMinutes(-45) < midnight ? midnight : end.AddMinutes(-45);
        return (start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss"), (int)(end - start).TotalMinutes);
    }

    [Fact]
    public async Task A_design_admin_sees_each_design_employees_logged_hours_and_project_mix_and_nothing_about_pay()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var today = CompanyToday();
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
                new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = thisMonth.AddMonths(-1).ToString("yyyy-MM-dd") }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var (start, end, minutes) = WorkToday();
        await Work.InsertAsync(designer.User.Id, project.Id, start, end);

        var response = await Client.GetAsync(UtilizationUrl(), admin.AccessToken);
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(200, body);
        var utilization = JsonSerializer.Deserialize<UtilizationResponse>(body, ApiClientExtensions.Json)!;

        utilization.Month.ShouldBe(thisMonth);
        utilization.InProgress.ShouldBeTrue();
        var person = utilization.People.Where(p => p.UserId == designer.User.Id).ShouldHaveSingleItem();
        person.ShouldSatisfyAllConditions(
            p => p.Hours.LoggedMinutes.ShouldBe(minutes),
            p => p.Hours.AvailableMinutes.ShouldBe(Math.Max(0, p.Hours.ExpectedMinutes - p.Hours.ExcusedMinutes)),
            p => p.Department.ShouldNotBeNull());
        var mix = person.Projects.ShouldHaveSingleItem();
        mix.ShouldSatisfyAllConditions(
            m => m.ProjectId.ShouldBe(project.Id),
            m => m.Title.ShouldBe(project.Title),
            m => m.Minutes.ShouldBe(minutes),
            m => m.Share.ShouldBe(1m));
        // admins log no hours and aren't listed
        utilization.People.ShouldNotContain(p => p.UserId == admin.User.Id);
        utilization.Departments.ShouldContain(d => d.Name == person.Department && d.People >= 1);

        // the same month asked by any of its days
        (await (await Client.GetAsync(UtilizationUrl(today), admin.AccessToken)).ReadAsync<UtilizationResponse>())
            .People.Single(p => p.UserId == designer.User.Id).Hours.LoggedMinutes.ShouldBe(minutes);

        // hours only: no cost, rate, pay, shortfall or made-up hours anywhere, the person's figures included
        using var json = JsonDocument.Parse(body);
        var money = new Regex("cost|rate|usd|pay|short|madeUp", RegexOptions.IgnoreCase);
        var mine = json.RootElement.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("userId").GetInt64() == designer.User.Id);
        PropertyNames(mine).ShouldNotContain(n => money.IsMatch(n));
        PropertyNames(json.RootElement).ShouldNotContain(n => money.IsMatch(n));
        mine.EnumerateObject().Select(p => p.Name).ShouldBe(["userId", "name", "department", "hours", "projects"], ignoreOrder: true);
        mine.GetProperty("hours").EnumerateObject().Select(p => p.Name).ShouldBe(
            ["loggedMinutes", "expectedMinutes", "excusedMinutes", "availableMinutes", "overtimeMinutes", "utilization"], ignoreOrder: true);
    }

    [Fact]
    public async Task Employees_cannot_read_utilization_and_admins_who_cannot_see_design_do_not_find_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);

        await (await Client.GetAsync(UtilizationUrl(), employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(UtilizationUrl(), executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_future_month_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var today = CompanyToday();
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);

        await (await Client.GetAsync(UtilizationUrl(nextMonth), superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // a past month is shown, and is over
        var lastMonth = nextMonth.AddMonths(-2);
        var past = await (await Client.GetAsync(UtilizationUrl(lastMonth.AddDays(14)), superAdmin.AccessToken)).ReadAsync<UtilizationResponse>();
        past.Month.ShouldBe(lastMonth);
        past.InProgress.ShouldBeFalse();
    }
}
