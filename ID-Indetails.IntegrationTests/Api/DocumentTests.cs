using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class DocumentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<(byte[] Content, string FileName)> PdfAsync(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var content = await response.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(content, 0, 5).ShouldBe("%PDF-");
        return (content, response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
    }

    [Fact]
    public async Task An_invoice_downloads_as_a_pdf_and_is_emailed_with_it_once_issued()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Concept design", amount = 1200m } } }, admin.AccessToken);
        var draft = await created.ReadAsync<InvoiceResponse>();

        (await PdfAsync($"/api/invoices/{draft.Id}/pdf", admin.AccessToken)).FileName.ShouldBe($"invoice-draft-{draft.Id}.pdf");
        ((int)(await Client.GetAsync($"/api/invoices/{draft.Id}/pdf", designer.AccessToken)).StatusCode).ShouldBe(403);
        await (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/email", new { to = "client@test.local" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceNotIssued);

        var issued = await (await Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", admin.AccessToken)).ReadAsync<InvoiceResponse>();
        await (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/email", new { to = "not an email" }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var sent = await Client.PostJsonAsync($"/api/invoices/{draft.Id}/email", new { to = " client@test.local ", note = "First send" }, admin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(201, await sent.Content.ReadAsStringAsync());
        (await sent.ReadAsync<InvoiceResponse>()).Deliveries.Single().ShouldSatisfyAllConditions(
            d => d.Channel.ShouldBe(DeliveryChannel.Email),
            d => d.Recipient.ShouldBe("client@test.local"),
            d => d.Note.ShouldBe("First send"));

        var email = api.Emails.LastTo("client@test.local");
        email.Subject.ShouldBe($"Invoice {issued.Number} - {project.Title}");
        email.TextBody.ShouldContain("$1,200.00");
        var attachment = email.Attachments!.Single();
        attachment.FileName.ShouldBe(issued.Number!.ToLowerInvariant() + ".pdf");
        attachment.MimeType.ShouldBe("application/pdf");
        System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 5).ShouldBe("%PDF-");

        // A failed send records nothing.
        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/email", new { to = "client@test.local" }, admin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);
        (await (await Client.GetAsync($"/api/invoices/{draft.Id}", admin.AccessToken)).ReadAsync<InvoiceResponse>()).Deliveries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_client_boq_downloads_as_a_pdf_and_is_emailed_with_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        var boqs = new BoqTestSteps(_steps);
        var boq = await boqs.CreateAsync(admin.AccessToken, project.Id);
        await boqs.SaveAsync(admin.AccessToken, boq.Id, [BoqTestSteps.Line("Concrete slab", 10, 120.5m, waste: 0.05m, code: "2.1.1", unit: "m3")]);

        (await PdfAsync($"/api/boqs/{boq.Id}/pdf", admin.AccessToken)).FileName.ShouldBe(boq.Number!.ToLowerInvariant() + ".pdf");

        var sent = await Client.PostJsonAsync($"/api/boqs/{boq.Id}/email", new { to = "site-client@test.local" }, admin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(200, await sent.Content.ReadAsStringAsync());
        (await sent.ReadAsync<BoqDetailResponse>()).Deliveries.Single().ShouldSatisfyAllConditions(
            d => d.Channel.ShouldBe(DeliveryChannel.Email),
            d => d.Recipient.ShouldBe("site-client@test.local"));

        var email = api.Emails.LastTo("site-client@test.local");
        email.Subject.ShouldBe($"Bill of quantities {boq.Number} - {project.Title}");
        email.Attachments!.Single().FileName.ShouldBe(boq.Number!.ToLowerInvariant() + ".pdf");

        // A failed send records nothing.
        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/email", new { to = "site-client@test.local" }, admin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);
        (await (await Client.GetAsync($"/api/boqs/{boq.Id}", admin.AccessToken)).ReadAsync<BoqDetailResponse>()).Deliveries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_design_project_s_report_downloads_internal_or_for_the_client()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var work = new WorkEntryTestSteps(_steps);
        await work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);
        await work.LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 90);
        var draft = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Deposit", amount = 500m } } }, admin.AccessToken)).ReadAsync<InvoiceResponse>();
        await Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", admin.AccessToken);

        var (_, internalName) = await PdfAsync($"/api/projects/{project.Id}/report", admin.AccessToken);
        internalName.ShouldStartWith($"project-{project.Id}-report-");
        var (_, clientName) = await PdfAsync($"/api/projects/{project.Id}/report?kind=client&sections=summary&sections=log", admin.AccessToken);
        clientName.ShouldStartWith($"project-{project.Id}-client-summary-");

        ((int)(await Client.GetAsync($"/api/projects/{project.Id}/report", designer.AccessToken)).StatusCode).ShouldBe(403);
        await (await Client.GetAsync($"/api/projects/{project.Id}/report?from=2026-05-02&to=2026-05-01", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await (await Client.GetAsync($"/api/projects/{site.Id}/report", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }
}
