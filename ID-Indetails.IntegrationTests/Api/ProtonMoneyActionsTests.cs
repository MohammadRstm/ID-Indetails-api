using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Actions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Money, deletes and pay rules switched on, as an admin would one by one (decisions 4, 5 and 8).</summary>
public sealed class ProtonMoneyApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Proton:Actions:Money", "true");
        builder.UseSetting("Proton:Actions:Deletes", "true");
        builder.UseSetting("Proton:Actions:PayRules", "true");
        builder.UseSetting("Proton:Actions:MaxConfirmedActionsPerHour", "200");
    }
}

/// <summary>
/// The money actions, with a scripted model: each is strong (two steps, and the amount or invoice number typed where
/// money is recorded or something can't be undone), goes through the existing service, and never carries bank details.
/// </summary>
public class ProtonMoneyActionsTests(ProtonMoneyApiFactory api) : IClassFixture<ProtonMoneyApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Please make that change." }, who.AccessToken);
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

    private async Task<T> GetAsync<T>(AuthResponse who, string path) => await (await Client.GetAsync(path, who.AccessToken)).ReadAsync<T>();

    [Fact]
    public async Task An_invoice_goes_from_draft_to_paid_and_void_with_the_amounts_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PutJsonAsync("/api/billing-settings/design", new { invoicePrefix = "HL", defaultTermsDays = 30 }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var (draft, _) = await ProposeAsync(superAdmin, "propose_create_invoice",
            new { project_id = project.Id, lines = new[] { new { description = "Concept design", amount = 1500m }, new { description = "Drawings", amount = 1000m } } });
        draft.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        draft.PendingAction.Preview.Changes.Single(c => c.Label == "Total").After.ShouldBe("2,500");
        await ConfirmAsync(superAdmin, draft);
        var invoice = (await GetAsync<PagedResponse<InvoiceSummaryResponse>>(superAdmin, $"/api/invoices?projectId={project.Id}")).Items.ShouldHaveSingleItem();
        invoice.Status.ShouldBe(InvoiceStatus.Draft);

        var (issue, _) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "issue" });
        issue.PendingAction!.Preview.Phrase.ShouldBe("2500");
        await (await Client.PostJsonAsync($"/api/proton/actions/{issue.PendingAction.Id}/confirm", new { issue.ConversationId, typed = "2,500" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, issue, "2500");
        var issued = await GetAsync<InvoiceResponse>(superAdmin, $"/api/invoices/{invoice.Id}");
        issued.Number.ShouldNotBeNull();

        var (pay, _) = await ProposeAsync(superAdmin, "propose_record_payment", new { project_id = project.Id, amount = 1000m, invoice_id = invoice.Id, reference = "TRX-77" });
        pay.PendingAction!.Preview.Note!.ShouldContain("from USD 2,500 to USD 1,500");
        await ConfirmAsync(superAdmin, pay, "1000");
        (await GetAsync<InvoiceResponse>(superAdmin, $"/api/invoices/{invoice.Id}")).Paid.ShouldBe(1000m);

        var (_, paidVoid) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "void", reason = "Wrong client" });
        paidVoid["message"]!.GetValue<string>().ShouldContain("voided first");

        var payment = (await GetAsync<PagedResponse<PaymentResponse>>(superAdmin, $"/api/payments?projectId={project.Id}")).Items.ShouldHaveSingleItem();
        var (voidPayment, _) = await ProposeAsync(superAdmin, "propose_payment_step", new { payment_id = payment.Id, void_reason = "Bounced" });
        await ConfirmAsync(superAdmin, voidPayment, "1000");

        var (voidInvoice, _) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "void", reason = "Wrong client" });
        voidInvoice.PendingAction!.Preview.Phrase.ShouldBe(issued.Number);
        await ConfirmAsync(superAdmin, voidInvoice, issued.Number);
        (await GetAsync<InvoiceResponse>(superAdmin, $"/api/invoices/{invoice.Id}")).Status.ShouldBe(InvoiceStatus.Void);
    }

    [Fact]
    public async Task Billing_settings_change_without_ever_touching_the_bank_details()
    {
        const string iban = "LB62099900000001001901229114";
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        (await Client.PutJsonAsync("/api/billing-settings/execution", new
        {
            invoicePrefix = "EX", defaultTermsDays = 30, bankAccount = "001-77-123456", bankName = "Probe Bank", iban, swift = "PROBLBBE",
            remindersEnabled = false, reminderFirstAfterDays = 1, reminderEveryDays = 7, reminderMax = 3,
        }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var (change, result) = await ProposeAsync(superAdmin, "propose_billing_settings", new { domain = "execution", default_terms_days = 45, reminders_enabled = true });
        result.ToJsonString().ShouldNotContain(iban);
        result.ToJsonString().ShouldNotContain("Probe Bank");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>().ProtonActions
                .SingleAsync(a => a.PublicId == change.PendingAction!.Id);
            (stored.Arguments + stored.Preview).ShouldNotContain(iban);
            (stored.Arguments + stored.Preview).ShouldNotContain("001-77-123456");
        }

        await ConfirmAsync(superAdmin, change);
        var settings = await GetAsync<BillingSettingsResponse>(superAdmin, "/api/billing-settings/execution");
        settings.DefaultTermsDays.ShouldBe(45);
        settings.RemindersEnabled.ShouldBeTrue();
        settings.Iban.ShouldBe(iban);
        settings.BankAccount.ShouldBe("001-77-123456");
    }

    [Fact]
    public async Task Costs_company_expenses_deletes_and_pay_rules_each_go_through_their_service()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var projects = new ProjectTestSteps(_steps);
        var project = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var (expense, _) = await ProposeAsync(superAdmin, "propose_add_expense", new { project_id = project.Id, label = "Render farm", amount = 320.5m, category = "software" });
        expense.PendingAction!.Preview.Phrase.ShouldBe("320.5");
        await ConfirmAsync(superAdmin, expense, "320.5");
        var recorded = (await GetAsync<ExpenseListResponse>(superAdmin, $"/api/projects/{project.Id}/expenses")).Items.ShouldHaveSingleItem();
        var (remove, _) = await ProposeAsync(superAdmin, "propose_delete_expense", new { project_id = project.Id, expense_id = recorded.Id });
        await ConfirmAsync(superAdmin, remove, "320.5");
        (await GetAsync<ExpenseListResponse>(superAdmin, $"/api/projects/{project.Id}/expenses")).Items.ShouldBeEmpty();

        var group = (await GetAsync<List<OverheadGroupResponse>>(superAdmin, "/api/company/overhead/groups")).First();
        var name = "Studio rent " + Guid.NewGuid().ToString("N")[..5];
        var (rent, _) = await ProposeAsync(superAdmin, "propose_overhead", new { step = "add", name, group_id = group.Id, amount = 1800m });
        await ConfirmAsync(superAdmin, rent, "1800");
        var line = (await GetAsync<OverheadResponse>(superAdmin, "/api/company/overhead")).Lines.Single(l => l.Name == name);
        var (cancel, _) = await ProposeAsync(superAdmin, "propose_overhead", new { step = "cancel", id = line.Id });
        await ConfirmAsync(superAdmin, cancel, name);

        // a client with projects is archived, not deleted; one without is deleted with its name typed
        var used = project.Client!;
        var (_, refused) = await ProposeAsync(superAdmin, "propose_delete", new { kind = "client", id = used.Id });
        refused["message"]!.GetValue<string>().ShouldContain("archive it instead");
        var spare = await projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var (delete, _) = await ProposeAsync(superAdmin, "propose_delete", new { kind = "client", id = spare.Id });
        await ConfirmAsync(superAdmin, delete, spare.DisplayName);
        (await Client.GetAsync($"/api/clients/{spare.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var month = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM");
        var (excuse, _) = await ProposeAsync(superAdmin, "propose_hour_excuse", new { user_id = designer.User.Id, month, minutes = 90 });
        excuse.PendingAction!.Preview.Changes.Single(c => c.Label == "Excused").After.ShouldBe("1 h 30");
        await ConfirmAsync(superAdmin, excuse);

        var (week, _) = await ProposeAsync(superAdmin, "propose_working_time", new { work_days = new[] { 1, 2, 3, 4, 5 }, start = "08:30", end = "17:00", break_minutes = 60 });
        week.PendingAction!.Preview.Reach!.ShouldStartWith("Affects ");
    }

    [Fact]
    public async Task An_invoice_card_shows_all_it_prints_and_a_total_that_changed_since_the_card_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PutJsonAsync("/api/billing-settings/design", new { invoicePrefix = "HN", defaultTermsDays = 30 }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // the notes are printed on the invoice for the client, so the card shows them in full; a line's text is a value, never a label
        const string notes = "Our bank details changed: pay to IBAN XX00 1234.";
        var (draft, _) = await ProposeAsync(superAdmin, "propose_create_invoice",
            new { project_id = project.Id, lines = new[] { new { description = "Total", amount = 1m }, new { description = "Drawings", amount = 2499m } }, notes });
        var changes = draft.PendingAction!.Preview.Changes;
        changes.Select(c => c.Label).ShouldBe(["Project", "Bill to", "Lines", "Total", "Notes printed for the client"]);
        changes.Single(c => c.Label == "Lines").After.ShouldBe("Total: 1; Drawings: 2,499");
        changes.Single(c => c.Label == "Total").After.ShouldBe("2,500");
        changes.Single(c => c.Label == "Notes printed for the client").After.ShouldBe(notes);
        await ConfirmAsync(superAdmin, draft);
        var invoice = (await GetAsync<PagedResponse<InvoiceSummaryResponse>>(superAdmin, $"/api/invoices?projectId={project.Id}")).Items.ShouldHaveSingleItem();

        // issuing shows what goes to the client too
        var (issue, _) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "issue" });
        issue.PendingAction!.Preview.Changes.Single(c => c.Label == "Notes printed for the client").After.ShouldBe(notes);
        issue.PendingAction.Preview.Changes.Single(c => c.Label == "Lines").After.ShouldContain("Drawings");

        // someone changes the draft before Confirm: the total the user types is the card's, so the step is refused
        (await Client.PutJsonAsync($"/api/invoices/{invoice.Id}",
            new { lines = new[] { new { description = "Drawings", amount = 9000m } }, termsDays = 30, notes }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/proton/actions/{issue.PendingAction.Id}/confirm", new { issue.ConversationId, typed = "2500" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        (await GetAsync<InvoiceResponse>(superAdmin, $"/api/invoices/{invoice.Id}")).Status.ShouldBe(InvoiceStatus.Draft);

        // a discard is held to the same rule
        var (discard, _) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "discard" });
        (await Client.PutJsonAsync($"/api/invoices/{invoice.Id}",
            new { lines = new[] { new { description = "Drawings", amount = 100m } }, termsDays = 30 }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/proton/actions/{discard.PendingAction!.Id}/confirm", new { discard.ConversationId, typed = "9000" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        // unchanged, it goes through
        var (again, _) = await ProposeAsync(superAdmin, "propose_invoice_step", new { invoice_id = invoice.Id, step = "issue" });
        await ConfirmAsync(superAdmin, again, "100");
        (await GetAsync<InvoiceResponse>(superAdmin, $"/api/invoices/{invoice.Id}")).Number.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_statement_is_refused_when_the_sites_materials_changed_since_the_card()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (materials, _) = await ProposeAsync(superAdmin, "propose_add_materials", new { project_id = site.Id, items = new object[] { new { name = "Sand", amount = 80m } } });
        await ConfirmAsync(superAdmin, materials, "80");

        var (statement, _) = await ProposeAsync(superAdmin, "propose_create_statement", new { project_id = site.Id });
        statement.PendingAction!.Preview.Phrase.ShouldNotBeNull();

        // more materials arrive before Confirm: the statement would bill more than the total the user typed
        var (more, _) = await ProposeAsync(superAdmin, "propose_add_materials", new { project_id = site.Id, items = new object[] { new { name = "Gravel", amount = 50m } } });
        await ConfirmAsync(superAdmin, more, "50");
        await (await Client.PostJsonAsync($"/api/proton/actions/{statement.PendingAction.Id}/confirm",
                new { statement.ConversationId, typed = statement.PendingAction.Preview.Phrase }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
    }

    [Fact]
    public async Task A_site_records_materials_and_labour_then_states_them_to_the_client()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var (materials, _) = await ProposeAsync(superAdmin, "propose_add_materials", new
        {
            project_id = site.Id,
            items = new object[] { new { name = "Cement", quantity = 10m, unit = "bag", unit_cost = 12m }, new { name = "Sand", amount = 80m } },
        });
        materials.PendingAction!.Preview.Phrase.ShouldBe("200");
        await ConfirmAsync(superAdmin, materials, "200");

        var monday = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var (labour, _) = await ProposeAsync(superAdmin, "propose_submit_labour_week", new
        {
            project_id = site.Id, week_start = monday.ToString("yyyy-MM-dd"),
            days = new[] { new { date = monday.ToString("yyyy-MM-dd"), workers = 3, daily_pay = 25m } },
        });
        await ConfirmAsync(superAdmin, labour, "75");

        var (statement, _) = await ProposeAsync(superAdmin, "propose_create_statement", new { project_id = site.Id });
        var card = statement.PendingAction!;
        card.Preview.Note!.ShouldContain("2 material(s)");
        var outcome = await ConfirmAsync(superAdmin, statement, card.Preview.Phrase);
        outcome.Message.ShouldStartWith("Made statement");
    }
}
