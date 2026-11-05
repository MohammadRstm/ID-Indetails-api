using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The actions finished for Execution, with a scripted model: site expenses in LBP and under an account label, a
/// site's initial payment and a payment's label, general meetings changed and deleted, and an Execution site created
/// with its staff. Each still goes through its service and only on the user's Confirm.
/// </summary>
public class ProtonExecutionActionsTests(ProtonMoneyApiFactory api) : IClassFixture<ProtonMoneyApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <param name="said">What the user typed: money amounts on a card must come from it.</param>
    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var card = reply.PendingAction.ShouldNotBeNull();
        var response = await Client.PostJsonAsync($"/api/proton/actions/{card.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<AccountLabelResponse> LabelAsync(AuthResponse superAdmin)
        => OkAsync<AccountLabelResponse>(Client.PostJsonAsync("/api/account-labels", new { name = "Label " + Guid.NewGuid().ToString("N")[..8] }, superAdmin.AccessToken));

    [Fact]
    public async Task Site_expenses_are_filed_under_a_label_and_an_lbp_one_needs_a_rate_on_its_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var label = await LabelAsync(superAdmin);

        var (card, _) = await ProposeAsync(superAdmin, "propose_add_site_expenses",
            new { project_id = site.Id, items = new[] { new { name = "Rebar", amount = 300m, account_label = label.Name.ToUpperInvariant() } } },
            "Add the rebar, 300");
        card.PendingAction!.Preview.Changes.First().After!.ShouldContain($"under {label.Name}");
        await ConfirmAsync(superAdmin, card, "300");
        var rebar = (await OkAsync<SiteExpenseListResponse>(Client.GetAsync($"/api/projects/{site.Id}/site-expenses", superAdmin.AccessToken))).Items.ShouldHaveSingleItem();
        rebar.AccountLabel!.Id.ShouldBe(label.Id);

        // a label that doesn't exist is named back with the ones that do
        var (_, unknown) = await ProposeAsync(superAdmin, "propose_add_site_expenses",
            new { project_id = site.Id, items = new[] { new { name = "Sand", amount = 50m, account_label = "No such label " + Guid.NewGuid() } } }, "Sand, 50");
        unknown["message"]!.GetValue<string>().ShouldContain("There is no account label called");

        // LBP counts only with an exchange rate in force on its day: said on the card, not after the Confirm
        var (_, noRate) = await ProposeAsync(superAdmin, "propose_add_site_expenses",
            new { project_id = site.Id, currency = "LBP", items = new[] { new { name = "Cement", amount = 900_000m, spent_on = "2001-01-02" } } }, "Cement, 900000 LBP");
        noRate["message"]!.GetValue<string>().ShouldContain("There is no LBP exchange rate in force on");

        // a correction moves it to no label
        var (fix, _) = await ProposeAsync(superAdmin, "propose_site_expense_step",
            new { project_id = site.Id, expense_id = rebar.Id, step = "update", expense = new { account_label = "" } }, "Take the rebar off its label");
        fix.PendingAction!.Preview.Changes.Single(c => c.Label == "Account label").After.ShouldBe("none");
        await ConfirmAsync(superAdmin, fix);
        (await OkAsync<SiteExpenseListResponse>(Client.GetAsync($"/api/projects/{site.Id}/site-expenses", superAdmin.AccessToken))).Items.Single().AccountLabel.ShouldBeNull();
    }

    [Fact]
    public async Task A_sites_initial_payment_is_recorded_under_a_label_and_a_design_project_takes_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var label = await LabelAsync(superAdmin);

        var (card, _) = await ProposeAsync(superAdmin, "propose_record_payment",
            new { project_id = site.Id, amount = 400m, is_initial = true, account_label = label.Name }, "The client paid 400 up front");
        card.PendingAction!.Preview.Changes.Select(c => c.Label).ShouldContain("Initial payment");
        await ConfirmAsync(superAdmin, card, "400");
        var paid = (await OkAsync<PagedResponse<PaymentResponse>>(Client.GetAsync($"/api/payments?projectId={site.Id}", superAdmin.AccessToken))).Items.ShouldHaveSingleItem();
        (paid.IsInitial, paid.AccountLabel!.Id).ShouldBe((true, label.Id));

        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var (_, refused) = await ProposeAsync(superAdmin, "propose_record_payment", new { project_id = project.Id, amount = 400m, is_initial = true }, "400 up front");
        refused["message"]!.GetValue<string>().ShouldContain("Only Execution projects take an initial payment");
    }

    [Fact]
    public async Task A_general_meeting_is_changed_keeping_its_people_and_deleted_with_its_topic_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, attendee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(9);
        var topic = "Studio planning " + Guid.NewGuid().ToString("N")[..6];
        var meeting = await OkAsync<MeetingResponse>(Client.PostJsonAsync("/api/meetings",
            new { startsAt = start, endsAt = start.AddHours(1), topic, attendeeIds = new[] { attendee.User.Id } }, superAdmin.AccessToken));

        var (change, _) = await ProposeAsync(superAdmin, "propose_update_meeting", new { meeting_id = meeting.Id, minutes = 90 });
        await ConfirmAsync(superAdmin, change);
        var changed = (await OkAsync<List<MeetingResponse>>(Client.GetAsync("/api/meetings", superAdmin.AccessToken))).Single(m => m.Id == meeting.Id);
        (changed.EndsAt - changed.StartsAt).ShouldBe(TimeSpan.FromMinutes(90));
        changed.Attendees.ShouldHaveSingleItem().Id.ShouldBe(attendee.User.Id);

        var (delete, _) = await ProposeAsync(superAdmin, "propose_delete", new { kind = "meeting", id = meeting.Id });
        delete.PendingAction!.Preview.Phrase.ShouldBe(topic);
        await ConfirmAsync(superAdmin, delete, topic);
        (await OkAsync<List<MeetingResponse>>(Client.GetAsync("/api/meetings", superAdmin.AccessToken))).ShouldNotContain(m => m.Id == meeting.Id);
    }

    [Fact]
    public async Task An_execution_site_is_created_with_its_staff_or_without_any_yet()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var title = "Site " + Guid.NewGuid().ToString("N")[..8];

        // its staff can come later
        var (none, _) = await ProposeAsync(superAdmin, "propose_create_project", new { title = title + " later", client_id = client.Id });
        none.PendingAction!.Preview.Title.ShouldBe("Create an Execution site");
        var (_, wrong) = await ProposeAsync(superAdmin, "propose_create_project", new { title, client_id = client.Id, staff_ids = new[] { designer.User.Id } });
        wrong["message"]!.GetValue<string>().ShouldContain("isn't an active Execution employee");

        var (card, _) = await ProposeAsync(superAdmin, "propose_create_project", new { title, client_id = client.Id, staff_ids = new[] { rami.User.Id } });
        card.PendingAction!.Preview.Title.ShouldBe("Create an Execution site");
        var created = (await ConfirmAsync(superAdmin, card)).Refs.ShouldHaveSingleItem();
        var site = await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{created.Id}", superAdmin.AccessToken));
        (site.Title, site.Domain).ShouldBe((title, DomainCodes.Execution));
        site.Staff!.ShouldHaveSingleItem().Id.ShouldBe(rami.User.Id);
    }
}
