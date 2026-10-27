using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Charts and tables with a scripted model: a block is built from a dataset a tool returned, with the tool's own
/// figures; a form the data can't take is refused with the reason; a user's request for a table is honoured; money
/// columns reach only people who may see money; and the block is saved with the chat.
/// </summary>
public class ProtonPresentTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    // Three whole weeks, Monday 2 to Sunday 22 March 2026.
    private const string From = "2026-03-02";
    private const string To = "2026-03-22";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private Task<HttpResponseMessage> AskAsync(AuthResponse who, string text, Guid? conversationId = null)
        => Client.PostJsonAsync("/api/proton/messages", new { conversationId, text }, who.AccessToken);

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private JsonNode ToolResult(int request) => api.Anthropic.ToolResults(request).Single();

    private JsonNode Result(int request) => JsonNode.Parse(ToolResult(request)["content"]!.GetValue<string>())!;

    private static object Summary(ProjectResponse project) => new { period = "custom", from = From, to = To, project_id = project.Id };

    /// <summary>Anna logs 2, 1 and 3 hours on one project, on the Wednesday of each week.</summary>
    private async Task<(AuthResponse SuperAdmin, AuthResponse Anna, ProjectResponse Project)> HoursAsync()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-03-04 07:00:00", "2026-03-04 09:00:00");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-03-11 07:00:00", "2026-03-11 08:00:00");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-03-18 07:00:00", "2026-03-18 10:00:00");
        return (superAdmin, anna, project);
    }

    [Fact]
    public async Task A_chart_is_built_from_the_data_a_tool_returned_and_saved_with_the_chat()
    {
        var (superAdmin, _, project) = await HoursAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_time_summary", Summary(project)),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d1", @as = "columns", title = "Weekly hours" }, id: "toolu_2"),
            FakeAnthropic.Text("The last week was the busiest."));

        var reply = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, $"How did the hours on {project.Title} go in March?"));

        // the model is told each dataset's shape and the forms it fits; the values stay with the app
        var weekly = Result(1)["datasets"]!.AsArray()[0]!;
        weekly["id"]!.GetValue<string>().ShouldBe("d1");
        weekly["title"]!.GetValue<string>().ShouldBe("Hours by week");
        weekly["forms"]!.AsArray().Select(f => f!.GetValue<string>()).ShouldBe(["columns", "line", "table"]);
        weekly["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldBe(["week", "hours (hours)", "entries (count)"]);

        var block = reply.Blocks.ShouldHaveSingleItem();
        block.Type.ShouldBe(ProtonBlockType.Chart);
        block.Title.ShouldBe("Weekly hours");
        block.Chart!.Kind.ShouldBe(ProtonPresentForm.Columns);
        block.Chart.Axis.ShouldBe(ProtonColumnKind.Week);
        block.Source.Tool.ShouldBe("get_time_summary");
        block.Source.Dataset.ShouldBe("d1");
        // the work report's own weekly figures, not the model's
        var report = await OkAsync<WorkReportResponse>(Client.GetAsync($"/api/reports/work?from={From}&to={To}&projectId={project.Id}", superAdmin.AccessToken));
        block.Chart.Points.Select(p => p.Values.Single()).ShouldBe(report.Weekly.Select(w => (decimal?)Math.Round(w.Minutes / 60m, 1)));

        var chat = await OkAsync<ProtonConversationResponse>(Client.GetAsync($"/api/proton/conversations/{reply.ConversationId}", superAdmin.AccessToken));
        var saved = chat.Messages[1].Blocks.ShouldHaveSingleItem();
        saved.Title.ShouldBe("Weekly hours");
        saved.Chart!.Points.Select(p => p.Values.Single()).ShouldBe(block.Chart.Points.Select(p => p.Values.Single()));
    }

    [Fact]
    public async Task A_form_the_data_cant_take_is_refused_with_the_reason_and_a_dataset_stays_in_its_chat()
    {
        var (superAdmin, _, project) = await HoursAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_time_summary", Summary(project)),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d1", @as = "composition" }, id: "toolu_2"),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d9", @as = "table" }, id: "toolu_3"),
            FakeAnthropic.Text("Week by week: 2, 1 and 3 hours."));

        var reply = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, $"Hours on {project.Title} in March?"));

        reply.Blocks.ShouldBeEmpty();
        ToolResult(2)["is_error"]!.GetValue<bool>().ShouldBeTrue();
        Result(2)["message"]!.GetValue<string>().ShouldContain("aren't the parts of one whole");
        Result(3)["message"]!.GetValue<string>().ShouldContain("There is no dataset \"d9\"");

        // a new chat holds none of the old one's data
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("present_data", new { dataset = "d1", @as = "table" }),
            FakeAnthropic.Text("I need to read the hours first."));
        var other = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, "Show that as a table"));

        other.ConversationId.ShouldNotBe(reply.ConversationId);
        other.Blocks.ShouldBeEmpty();
        Result(5)["message"]!.GetValue<string>().ShouldContain("There is no dataset \"d1\"");
    }

    [Fact]
    public async Task Asked_for_a_table_Proton_is_sent_back_once_when_it_answers_without_one()
    {
        var (superAdmin, anna, project) = await HoursAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_time_summary", Summary(project)),
            FakeAnthropic.Text("Anna logged 6 hours."),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d3", @as = "table" }, id: "toolu_2"),
            FakeAnthropic.Text("That's the table."));

        var reply = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, $"Show the hours on {project.Title} by person in March as a table"));

        Result(1)["datasets"]!.AsArray()[2]!["title"]!.GetValue<string>().ShouldBe("Hours by person");
        var reminder = api.Anthropic.Requests[2]["messages"]!.AsArray().Last()!;
        reminder["role"]!.GetValue<string>().ShouldBe("user");
        reminder["content"]![0]!["text"]!.GetValue<string>().ShouldStartWith("[From the app] The user asked for a table.");
        reply.Reply.ShouldBe("Anna logged 6 hours.\n\nThat's the table.");

        var table = reply.Blocks.ShouldHaveSingleItem().Table.ShouldNotBeNull();
        table.Columns.Select(c => c.Key).ShouldBe(["person", "hours", "overtime", "entries"]);
        var row = table.Rows.ShouldHaveSingleItem();
        row.Cells[0].Text.ShouldNotBeNull().ShouldStartWith(anna.User.FirstName);
        row.Cells[1].Number.ShouldBe(6m);
        row.Ref!.Id.ShouldBe(anna.User.Id);
        api.Anthropic.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Money_columns_reach_only_people_who_may_see_money()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        async Task<(ProtonReplyResponse Reply, string Described)> MoneyTableFor(AuthResponse who)
        {
            api.Anthropic.Reset();
            api.Anthropic.Reply(
                FakeAnthropic.ToolUse("list_projects", new { domain = "design", search = project.Title }),
                FakeAnthropic.ToolUse("present_data", new { dataset = "d1", @as = "table", columns = new[] { "status", "agreed_revenue", "agreed_profit" } }, id: "toolu_2"),
                FakeAnthropic.Text("Here it is."));
            var reply = await OkAsync<ProtonReplyResponse>(AskAsync(who, $"List {project.Title} with its money as a table"));
            return (reply, Result(1)["datasets"]!.AsArray()[0]!["columns"]!.ToJsonString());
        }

        var (shown, described) = await MoneyTableFor(superAdmin);
        described.ShouldContain("agreed_revenue (usd)");
        var table = shown.Blocks.ShouldHaveSingleItem().Table!;
        table.Columns.Select(c => c.Key).ShouldBe(["project", "status", "agreed_revenue", "agreed_profit"]);
        table.Rows.ShouldHaveSingleItem().Cells[2].Number.ShouldBe(5000m);

        var (refused, describedToAdmin) = await MoneyTableFor(admin);
        describedToAdmin.ShouldContain("project");
        describedToAdmin.ShouldNotContain("revenue");
        describedToAdmin.ShouldNotContain("cost");
        describedToAdmin.ShouldNotContain("profit");
        refused.Blocks.ShouldBeEmpty();
        Result(2)["message"]!.GetValue<string>().ShouldContain("There is no column \"agreed_revenue\"");
    }

    [Fact]
    public async Task Money_by_month_is_the_financials_own_and_only_for_people_who_may_see_money()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_money_by_month", new { domain = "design", months = 6 }),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d1", @as = "line", columns = new[] { "received", "cost" } }, id: "toolu_2"),
            FakeAnthropic.Text("Cost has been steady."));

        var reply = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, "How has design money gone by month?"));

        var monthly = await OkAsync<List<FinanceMonthResponse>>(Client.GetAsync("/api/finance/design/monthly?months=6", superAdmin.AccessToken));
        var line = reply.Blocks.ShouldHaveSingleItem().Chart!;
        line.Kind.ShouldBe(ProtonPresentForm.Line);
        line.Axis.ShouldBe(ProtonColumnKind.Month);
        line.Series.Select(s => s.Key).ShouldBe(["received", "cost"]);
        line.Points.Select(p => p.Values[0]).ShouldBe(monthly.Select(m => (decimal?)m.Revenue));
        line.Points.Select(p => p.Values[1]).ShouldBe(monthly.Select(m => (decimal?)m.Cost));
        line.Points[^1].Partial.ShouldBeTrue();

        // without financial:view the tool isn't offered at all
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.Text("I can't see the money."));
        await OkAsync<ProtonReplyResponse>(AskAsync(admin, "Design money by month?"));
        api.Anthropic.ToolsOffered(0).ShouldNotContain("get_money_by_month");
    }

    /// <summary>The weeks and months a person's summary already holds are kept for charts, not thrown away.</summary>
    [Fact]
    public async Task A_persons_hours_by_month_can_be_charted()
    {
        var (superAdmin, anna, _) = await HoursAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_person_work", new { user_id = anna.User.Id, period = "custom", from = From, to = To }),
            FakeAnthropic.ToolUse("present_data", new { dataset = "d2", @as = "columns" }, id: "toolu_2"),
            FakeAnthropic.Text("March was her busiest month."));

        var reply = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, "How much has Anna worked each month?"));

        Result(1)["datasets"]!.AsArray().Select(d => d!["title"]!.GetValue<string>())
            .ShouldBe([$"{anna.User.FirstName} {anna.User.LastName}: hours by week", $"{anna.User.FirstName} {anna.User.LastName}: hours by month",
                $"{anna.User.FirstName} {anna.User.LastName}: hours by project"]);
        var chart = reply.Blocks.ShouldHaveSingleItem().Chart!;
        chart.Axis.ShouldBe(ProtonColumnKind.Month);
        chart.Points.Single(p => p.Label == "2026-03-01").Values.ShouldBe([6m]);
        chart.Points[^1].Partial.ShouldBeTrue();
    }
}
