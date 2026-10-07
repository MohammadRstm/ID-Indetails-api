using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class InvoiceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static object Lines(params decimal[] amounts)
        => new { lines = amounts.Select((a, i) => new { description = $"Stage {i + 1}", amount = a }).ToArray() };

    private async Task<InvoiceResponse> DraftAsync(long projectId, string token, params decimal[] amounts)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/invoices", Lines(amounts), token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<InvoiceResponse>();
    }

    private async Task<InvoiceResponse> IssueAsync(long invoiceId, string token, object? body = null)
    {
        var response = body is null
            ? await Client.PostEmptyAsync($"/api/invoices/{invoiceId}/issue", token)
            : await Client.PostJsonAsync($"/api/invoices/{invoiceId}/issue", body, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<InvoiceResponse>();
    }

    [Fact]
    public async Task A_draft_is_edited_issued_with_the_next_number_delivered_and_voided()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);

        var draft = await DraftAsync(project.Id, admin.AccessToken, 1000m, 500.25m);
        draft.Status.ShouldBe(InvoiceStatus.Draft);
        draft.Number.ShouldBeNull();
        draft.Total.ShouldBe(1500.25m);
        draft.TermsDays.ShouldBe(45);
        draft.CreatedBy!.Id.ShouldBe(admin.User.Id);
        draft.Client.Id.ShouldBe(project.Client!.Id);

        var edited = await (await Client.PutJsonAsync($"/api/invoices/{draft.Id}",
            new { lines = new[] { new { description = "Concept design", amount = 2000m } }, termsDays = 15, notes = "Thank you" },
            admin.AccessToken)).ReadAsync<InvoiceResponse>();
        edited.Lines.Single().Position.ShouldBe(1);
        edited.Total.ShouldBe(2000m);

        var issued = await IssueAsync(draft.Id, admin.AccessToken);
        // Issued today in the company's time zone, which is a day ahead of UTC late in the evening.
        var issuedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(issued.IssuedAt!.Value, "Asia/Beirut"));
        var year = issuedOn.Year;
        issued.Number.ShouldBe($"HL-{year}-001");
        issued.Status.ShouldBe(InvoiceStatus.Sent);
        issued.Balance.ShouldBe(2000m);
        issued.DueOn.ShouldBe(issuedOn.AddDays(15));

        var second = await IssueAsync((await DraftAsync(project.Id, admin.AccessToken, 10m)).Id, admin.AccessToken);
        second.Number.ShouldBe($"HL-{year}-002");

        await (await Client.PutJsonAsync($"/api/invoices/{issued.Id}", Lines(5m), admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceNotDraft);

        var delivered = await Client.PostJsonAsync($"/api/invoices/{issued.Id}/deliveries",
            new { channel = "email", recipient = "client@test.local" }, admin.AccessToken);
        ((int)delivered.StatusCode).ShouldBe(201);
        await Client.PostJsonAsync($"/api/invoices/{issued.Id}/deliveries", new { channel = "download" }, admin.AccessToken);
        (await delivered.ReadAsync<InvoiceResponse>()).Deliveries.Single().Channel.ShouldBe(DeliveryChannel.Email);

        await (await Client.PostJsonAsync($"/api/invoices/{issued.Id}/void", new { reason = " " }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var voided = await (await Client.PostJsonAsync($"/api/invoices/{issued.Id}/void", new { reason = "Wrong amount" }, admin.AccessToken))
            .ReadAsync<InvoiceResponse>();
        voided.Status.ShouldBe(InvoiceStatus.Void);
        voided.Number.ShouldBe($"HL-{year}-001");
        await (await Client.PostJsonAsync($"/api/invoices/{issued.Id}/deliveries", new { channel = "email" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceVoided);

        var list = await (await Client.GetAsync($"/api/invoices?projectId={project.Id}&status=void", admin.AccessToken))
            .ReadAsync<PagedResponse<InvoiceSummaryResponse>>();
        list.Items.Single().Id.ShouldBe(issued.Id);
    }

    [Fact]
    public async Task Drafts_are_discarded_and_past_issue_dates_number_in_their_own_year()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var draft = await DraftAsync(project.Id, superAdmin.AccessToken, 300m);

        await (await Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.BillingSettingsMissing);
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");
        await (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { issuedOn = tomorrow }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.IssueDateInFuture);

        var backdated = await IssueAsync(draft.Id, superAdmin.AccessToken, new { issuedOn = "2025-12-20" });
        backdated.Number.ShouldBe("EX-2025-001");
        // Drafted before the settings existed, so it kept the 30-day fallback terms.
        backdated.DueOn.ShouldBe(new DateOnly(2026, 1, 19));
        backdated.IsOverdue.ShouldBe(true);
        (await (await Client.GetAsync("/api/invoices?overdue=true&domain=execution", superAdmin.AccessToken))
            .ReadAsync<PagedResponse<InvoiceSummaryResponse>>()).Items.ShouldContain(i => i.Id == draft.Id);

        var other = await DraftAsync(project.Id, superAdmin.AccessToken, 50m);
        (await Client.DeleteAsync($"/api/invoices/{other.Id}", superAdmin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.GetAsync($"/api/invoices/{other.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/invoices/{draft.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.InvoiceNotDraft);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'invoice' AND subject_id = @id AND kind = 'invoice_discarded'",
            ("@id", other.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Internal_projects_are_not_invoiced()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var internalProject = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Studio upgrade", billingType = "internal", domainCode = DomainCodes.Design }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();

        await (await Client.PostJsonAsync($"/api/projects/{internalProject.Id}/invoices", Lines(10m), superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectNotBillable);
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_reach_invoices()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var draft = await DraftAsync(project.Id, superAdmin.AccessToken, 100m);

        await (await Client.GetAsync("/api/invoices", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices", Lines(1m), employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await (await Client.GetAsync($"/api/invoices/{draft.Id}", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices", Lines(1m), executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await (await Client.GetAsync("/api/invoices?pageSize=100", executionAdmin.AccessToken))
            .ReadAsync<PagedResponse<InvoiceSummaryResponse>>()).Items.ShouldAllBe(i => i.Domain == DomainCodes.Execution);
    }

    [Fact]
    public async Task Senders_without_financial_view_never_see_what_was_paid()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, sender) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        (await Client.PutJsonAsync($"/api/users/{sender.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.InvoiceSend, action = "grant", reason = "Sends invoices only" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var invoice = await IssueAsync((await DraftAsync(project.Id, superAdmin.AccessToken, 1000m)).Id, superAdmin.AccessToken);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = 400m, currency = "USD", invoiceId = invoice.Id }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var response = await Client.PostJsonAsync($"/api/invoices/{invoice.Id}/deliveries", new { channel = "download" }, sender.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadAsStringAsync();
        json.ShouldNotContain("\"paid\"");
        json.ShouldNotContain("\"balance\"");
        json.ShouldNotContain("\"isOverdue\"");
        var seen = await response.ReadAsync<InvoiceResponse>();
        seen.Status.ShouldBe(InvoiceStatus.Sent);
        seen.Total.ShouldBe(1000m);

        var full = await (await Client.GetAsync($"/api/invoices/{invoice.Id}", superAdmin.AccessToken)).ReadAsync<InvoiceResponse>();
        full.Status.ShouldBe(InvoiceStatus.Partial);
        full.Balance.ShouldBe(600m);
    }
}
