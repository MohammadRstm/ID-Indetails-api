using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton's utilization tool, with a scripted model: each Design employee's logged against expected hours and where
/// they went, hours only. It needs report:view (the endpoint's own policy) and Design: admins have both by default,
/// an Execution admin isn't offered it, and nothing in it is money.
/// </summary>
public class ProtonUtilizationTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>One tool call on its own question: the tool's result (its data, basis and datasets, or a refusal's status), and the reply.</summary>
    private async Task<(JsonNode Result, ProtonReplyResponse Reply)> CallAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here it is."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "How utilized is the team?" }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var reply = await response.ReadAsync<ProtonReplyResponse>();
        return (JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!, reply);
    }

    /// <summary>
    /// Up to 45 minutes of work ending a couple of minutes ago, kept within today in Beirut so it is in this month and
    /// on or after a person invited today joined.
    /// </summary>
    private static (string Start, string End, int Minutes) WorkToday()
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var now = DateTime.UtcNow;
        var end = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(-2);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, beirut));
        var midnight = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), beirut);
        var start = end.AddMinutes(-45) < midnight ? midnight : end.AddMinutes(-45);
        return (start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss"), (int)(end - start).TotalMinutes);
    }

    /// <summary>Every property name anywhere in a JSON value, nested objects and arrays included.</summary>
    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(p => PropertyNames(p.Value).Prepend(p.Key)),
        JsonArray a => a.SelectMany(PropertyNames),
        _ => [],
    };

    [Fact]
    public async Task A_design_admin_reads_each_design_employees_hours_and_project_mix_and_nothing_about_pay()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
                new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = thisMonth.AddMonths(-1).ToString("yyyy-MM-dd") }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var (start, end, minutes) = WorkToday();
        await new WorkEntryTestSteps(_steps).InsertAsync(designer.User.Id, project.Id, start, end);

        // an admin holds report:view by default, without financial:view
        var (utilization, reply) = await CallAsync(admin, "analyze_employee_utilization", new { });
        api.Anthropic.ToolsOffered(0).ShouldContain("analyze_employee_utilization");
        var data = utilization["data"]!;
        data["month"]!.GetValue<string>().ShouldBe(thisMonth.ToString("yyyy-MM-dd"));
        data["in_progress"]!.GetValue<bool>().ShouldBeTrue();
        var person = data["people"]!.AsArray().Single(p => p!["user_id"]!.GetValue<long>() == designer.User.Id)!;
        person["hours"]!["logged_hours"]!.GetValue<decimal>().ShouldBe(Math.Round(minutes / 60m, 1));
        var mix = person["projects"]!.AsArray().ShouldHaveSingleItem()!;
        (mix["project_id"]!.GetValue<long>(), mix["share_pct"]!.GetValue<int>()).ShouldBe((project.Id, 100));
        person["more_projects"]!.GetValue<int>().ShouldBe(0);
        // admins log no hours and aren't listed
        data["people"]!.AsArray().ShouldNotContain(p => p!["user_id"]!.GetValue<long>() == admin.User.Id);
        data["note"]!.GetValue<string>().ShouldContain("never link it to pay");

        // the basis says the rule; each person's and each department's hours as tables
        reply.Sources.ShouldHaveSingleItem().Label.ShouldStartWith("Design utilization");
        utilization["basis"]!["rules"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldContain(r => r.Contains("logged ÷ (expected − excused)"));
        var titles = utilization["datasets"]!.AsArray().Select(d => d!["title"]!.GetValue<string>()).ToList();
        titles.ShouldContain("Design: utilization by person");
        titles.ShouldContain("Design: utilization by department");

        // hours only: no cost, rate, pay, shortfall or made-up hours anywhere, the person's figures included
        var money = new Regex("cost|rate|usd|pay|short|made_up|salary", RegexOptions.IgnoreCase);
        PropertyNames(data).ShouldNotContain(n => money.IsMatch(n));
        utilization["datasets"]!.ToJsonString().ShouldNotContain("(usd)");
    }

    [Fact]
    public async Task Without_report_view_or_design_the_tool_is_refused_and_a_future_month_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // report:view taken away: the endpoint's policy keeps the tool from them, offered or asked for
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var revoked = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ReportView, action = "revoke", reason = "No reports" } } }, superAdmin.AccessToken);
        revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync());
        (await CallAsync(admin, "analyze_employee_utilization", new { })).Result["status"]!.GetValue<string>().ShouldBe("forbidden");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("analyze_employee_utilization");

        // an Execution admin has report:view but not Design, whose hours these are
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        (await CallAsync(executionAdmin, "analyze_employee_utilization", new { })).Result["status"]!.GetValue<string>().ShouldBe("forbidden");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("analyze_employee_utilization");

        // a future month is refused as the endpoint refuses it; a past one is over
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var (future, _) = await CallAsync(superAdmin, "analyze_employee_utilization", new { month = nextMonth.ToString("yyyy-MM-dd") });
        future["status"]!.GetValue<string>().ShouldBeOneOf("invalid", "refused");
        future["message"]!.GetValue<string>().ShouldContain("future month");
        var lastMonth = nextMonth.AddMonths(-2);
        var past = (await CallAsync(superAdmin, "analyze_employee_utilization", new { month = lastMonth.AddDays(14).ToString("yyyy-MM-dd") })).Result;
        (past["data"]!["month"]!.GetValue<string>(), past["data"]!["in_progress"]!.GetValue<bool>()).ShouldBe((lastMonth.ToString("yyyy-MM-dd"), false));
        past["basis"]!["partial"]!.GetValue<bool>().ShouldBeFalse();
    }
}
