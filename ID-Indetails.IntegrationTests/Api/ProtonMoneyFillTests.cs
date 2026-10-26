using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Money and pay forms, with those families switched on: an amount fills only when the user typed it in this message,
/// and a payment only on its own project's invoice. Nothing is saved.
/// </summary>
public class ProtonMoneyFillTests(ProtonMoneyApiFactory api) : IClassFixture<ProtonMoneyApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<ProtonReplyResponse> AskAsync(AuthResponse who, string text)
    {
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    private JsonNode Result(int request) => JsonNode.Parse(api.Anthropic.ToolResults(request).Single()["content"]!.GetValue<string>())!;

    [Fact]
    public async Task A_payment_fills_with_the_amount_the_user_typed_and_never_another()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            // an amount the user didn't type (worked out, or from the data), then the one they did, written their way
            FakeAnthropic.ToolUse("fill_form", new { form = "payment", id = project.Id, payment = new { amount = 2000m, currency = "USD" } }),
            FakeAnthropic.ToolUse("fill_form", new { form = "payment", id = project.Id, payment = new { amount = 1500m, currency = "USD", reference = "TRX-9" } }),
            FakeAnthropic.Text("I've filled the payment. Check it and record it."));

        var reply = await AskAsync(superAdmin, "Record a payment of 1,500 on this project, reference TRX-9");

        Result(1)["status"]!.GetValue<string>().ShouldBe("invalid");
        Result(2)["status"]!.GetValue<string>().ShouldBe("ok");
        var fill = reply.UiCommands.ShouldHaveSingleItem().Fill!;
        fill.Form.ShouldBe(ProtonForm.Payment);
        fill.Payment!.Amount.ShouldBe(1500m);

        // filled, not recorded
        var payments = await (await Client.GetAsync($"/api/payments?projectId={project.Id}", superAdmin.AccessToken)).ReadAsync<JsonNode>();
        payments["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_payment_names_only_an_invoice_of_its_own_project()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var projects = new ProjectTestSteps(_steps);
        var project = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var other = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var draft = await Client.PostJsonAsync($"/api/projects/{other.Id}/invoices",
            new { lines = new[] { new { description = "Concept", amount = 900m } }, termsDays = 30 }, superAdmin.AccessToken);
        draft.IsSuccessStatusCode.ShouldBeTrue(await draft.Content.ReadAsStringAsync());
        var invoiceId = (await draft.ReadAsync<JsonNode>())["id"]!.GetValue<long>();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("fill_form", new { form = "payment", id = project.Id, payment = new { invoice_id = invoiceId, amount = 900m } }),
            FakeAnthropic.Text("That invoice isn't this project's."));

        var reply = await AskAsync(superAdmin, "Record 900 against that invoice");

        Result(1)["status"]!.GetValue<string>().ShouldNotBe("ok");
        reply.UiCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_working_week_fills_while_pay_rules_are_on()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("fill_form", new
            {
                form = "schedule", id = colleague.User.Id,
                schedule = new { days = new[] { new { weekday = 1, start_minute = 540, end_minute = 1020 }, new { weekday = 2, start_minute = 540, end_minute = 1020 } } },
            }),
            FakeAnthropic.Text("I've set Monday and Tuesday, 9 to 5. Check it and save."));

        var reply = await AskAsync(superAdmin, $"{colleague.User.FirstName} works Mondays and Tuesdays 9 to 5 now");

        Result(1)["status"]!.GetValue<string>().ShouldBe("ok");
        var fill = reply.UiCommands.ShouldHaveSingleItem().Fill!;
        fill.Id.ShouldBe(colleague.User.Id);
        fill.Schedule!.Days!.Count.ShouldBe(2);
    }
}
