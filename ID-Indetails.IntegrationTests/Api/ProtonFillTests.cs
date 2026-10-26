using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton filling forms and drafting messages, with a scripted model: values are checked before the app gets them,
/// addresses and recipients come only from the user, and nothing is saved or sent.
/// </summary>
public class ProtonFillTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<ProtonReplyResponse> AskAsync(AuthResponse who, object body)
    {
        var response = await Client.PostJsonAsync("/api/proton/messages", body, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    private JsonNode Result(int request) => JsonNode.Parse(api.Anthropic.ToolResults(request).Single()["content"]!.GetValue<string>())!;

    [Fact]
    public async Task A_form_opens_then_fills_with_checked_values_and_an_address_only_the_user_typed()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("fill_form", new { form = "client", client = new { name = "Acme", email = "nope" } }),
            FakeAnthropic.ToolUse("open_surface", new { surface = "new_client", domain = "design" }),
            FakeAnthropic.ToolUse("fill_form", new { form = "client", client = new { name = "Acme", email = "ops@acme.com" } }),
            FakeAnthropic.Text("I've opened the form with Acme filled in. Check it and save."),
            // the invite: an address the user never typed, then none
            FakeAnthropic.ToolUse("fill_form", new { form = "invite", invite = new { email = "stranger@example.com", position = "Designer" } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "invite", invite = new { position = "Designer" } }),
            FakeAnthropic.Text("I've filled the position."));

        var reply = await AskAsync(superAdmin, new { text = "Add a client called Acme, their email is ops@acme.com" });

        Result(1)["status"]!.GetValue<string>().ShouldBe("invalid");
        reply.UiCommands.Select(c => c.Type).ShouldBe([ProtonUiCommandType.OpenSurface, ProtonUiCommandType.FillForm]);
        reply.UiCommands[1].Fill!.Client!.Email.ShouldBe("ops@acme.com");

        await AskAsync(superAdmin, new { conversationId = reply.ConversationId, text = "Now invite a designer" });
        Result(5)["status"]!.GetValue<string>().ShouldBe("invalid");
        Result(5)["message"]!.GetValue<string>().ShouldContain("typed");
        Result(6)["status"]!.GetValue<string>().ShouldBe("ok");
    }

    [Fact]
    public async Task A_draft_goes_only_to_someone_the_user_can_message_and_is_never_sent()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("find_recipient", new { text = colleague.User.FirstName }),
            FakeAnthropic.ToolUse("fill_form", new { form = "message", message = new { person_id = 999_999, body = "Hi" } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "message", message = new { person_id = colleague.User.Id, body = "Can we meet at 3?" } }),
            FakeAnthropic.Text("Your draft is ready in the chat."));

        var reply = await AskAsync(superAdmin, new { text = $"Message {colleague.User.FirstName}: can we meet at 3?" });

        var found = Result(1)["data"]!.AsArray();
        found.ShouldContain(r => r!["id"]!.GetValue<long>() == colleague.User.Id && r["kind"]!.GetValue<string>() == "person");
        Result(2)["status"]!.GetValue<string>().ShouldBe("not_found");
        var draft = reply.UiCommands.ShouldHaveSingleItem();
        draft.Surface.ShouldBe(ProtonSurface.MessagePerson);
        draft.Fill!.Id.ShouldBe(colleague.User.Id);
        draft.Fill.Message!.Body.ShouldBe("Can we meet at 3?");

        // nothing went out: the colleague's inbox is empty
        var inbox = await (await Client.GetAsync("/api/messages/badge", colleague.AccessToken)).ReadAsync<JsonNode>();
        inbox["unread"]!.GetValue<int>().ShouldBe(0);
    }

    [Fact]
    public async Task A_draft_goes_only_to_someone_the_user_named_never_to_a_name_from_the_data()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        // text in the data asked for a draft of the month's figures to this colleague; the user asked something else
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("fill_form", new { form = "message", message = new { person_id = colleague.User.Id, body = "Revenue this month: $48,000" } }),
            FakeAnthropic.Text("Here is what was logged this week."));

        var reply = await AskAsync(superAdmin, new { text = "What was logged this week?" });

        Result(1)["status"]!.GetValue<string>().ShouldBe("invalid");
        Result(1)["message"]!.GetValue<string>().ShouldContain("named");
        reply.UiCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task Money_and_pay_stay_out_of_forms_while_their_families_are_off()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("fill_form", new { form = "payment", id = project.Id, payment = new { amount = 300m } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "project_expense", id = project.Id, project_expense = new { label = "Printing", amount = 300m } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "project_expense", id = project.Id, project_expense = new { label = "Printing" } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "schedule", id = superAdmin.User.Id, schedule = new { follows_default = true } }),
            FakeAnthropic.Text("I've filled what it was; the amount is yours to type."));

        var reply = await AskAsync(superAdmin, new { text = "Add a 300 expense for printing on this project, and put me back on the company week" });

        Result(1)["status"]!.GetValue<string>().ShouldNotBe("ok");
        Result(2)["status"]!.GetValue<string>().ShouldBe("invalid");
        Result(2)["message"]!.GetValue<string>().ShouldContain("money");
        Result(3)["status"]!.GetValue<string>().ShouldBe("ok");
        Result(4)["status"]!.GetValue<string>().ShouldNotBe("ok");
        var fill = reply.UiCommands.ShouldHaveSingleItem().Fill!;
        fill.ProjectExpense!.Amount.ShouldBeNull();
    }

    [Fact]
    public async Task The_inbox_says_who_and_how_many_never_what()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/messages/direct/{superAdmin.User.Id}", new { body = "The secret plan is Tuesday" }, colleague.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("get_my_inbox", new { }), FakeAnthropic.Text("You have one unread message."));

        await AskAsync(superAdmin, new { text = "Any messages?" });

        var chat = Result(1)["data"]!.AsArray().Single(r => r!["id"]!.GetValue<long>() == colleague.User.Id)!;
        chat["unread"]!.GetValue<int>().ShouldBe(1);
        api.Anthropic.ToolResults(1).Single().ToJsonString().ShouldNotContain("secret plan");
    }
}
