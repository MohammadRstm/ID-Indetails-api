using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class PaymentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<PaymentResponse> RecordAsync(long projectId, string token, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", body, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PaymentResponse>();
    }

    private async Task<InvoiceResponse> IssuedInvoiceAsync(long projectId, string token, decimal amount)
    {
        var draft = await (await Client.PostJsonAsync($"/api/projects/{projectId}/invoices",
            new { lines = new[] { new { description = "Stage", amount } } }, token)).ReadAsync<InvoiceResponse>();
        return await (await Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", token)).ReadAsync<InvoiceResponse>();
    }

    private async Task<InvoiceResponse> InvoiceAsync(long invoiceId, string token)
        => await (await Client.GetAsync($"/api/invoices/{invoiceId}", token)).ReadAsync<InvoiceResponse>();

    [Fact]
    public async Task Concurrent_payments_never_overpay_an_invoice()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 50_000m);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);

        // Applying a payment on account races recording a new one: only one of them fits.
        for (var round = 0; round < 5; round++)
        {
            var invoice = await IssuedInvoiceAsync(project.Id, admin.AccessToken, 1000m);
            var onAccount = await RecordAsync(project.Id, admin.AccessToken, new { amount = 700m, currency = "USD" });

            var results = await Task.WhenAll(
                Client.PutJsonAsync($"/api/payments/{onAccount.Id}/invoice", new { invoiceId = invoice.Id }, admin.AccessToken),
                Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
                    new { amount = 700m, currency = "USD", invoiceId = invoice.Id }, admin.AccessToken));

            results.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
            (await InvoiceAsync(invoice.Id, admin.AccessToken)).Balance.ShouldBe(300m);
        }
    }

    [Fact]
    public async Task Payments_need_no_invoice_and_settle_issued_invoices_up_to_their_balance()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // Nothing invoiced or sent yet: the payment is simply held on account.
        var onAccount = await RecordAsync(project.Id, admin.AccessToken, new { amount = 2000m, currency = "USD", method = "cash" });
        onAccount.InvoiceId.ShouldBeNull();
        onAccount.AmountUsd.ShouldBe(2000m);

        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);
        var invoice = await IssuedInvoiceAsync(project.Id, admin.AccessToken, 3000m);

        var partial = await RecordAsync(project.Id, admin.AccessToken, new { amount = 1000m, currency = "USD", invoiceId = invoice.Id, reference = "TRX-1" });
        partial.InvoiceNumber.ShouldBe(invoice.Number);
        var afterPartial = await InvoiceAsync(invoice.Id, admin.AccessToken);
        afterPartial.Status.ShouldBe(InvoiceStatus.Partial);
        afterPartial.Balance.ShouldBe(2000m);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 2000.01m, currency = "USD", invoiceId = invoice.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PaymentExceedsBalance);

        // The earlier payment on account settles the rest.
        var applied = await (await Client.PutJsonAsync($"/api/payments/{onAccount.Id}/invoice", new { invoiceId = invoice.Id }, admin.AccessToken))
            .ReadAsync<PaymentResponse>();
        applied.InvoiceId.ShouldBe(invoice.Id);
        (await InvoiceAsync(invoice.Id, admin.AccessToken)).Status.ShouldBe(InvoiceStatus.Paid);
        await (await Client.PutJsonAsync($"/api/payments/{onAccount.Id}/invoice", new { invoiceId = invoice.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PaymentAlreadyApplied);

        await (await Client.PostJsonAsync($"/api/invoices/{invoice.Id}/void", new { reason = "Mistake" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceHasPayments);

        var voided = await (await Client.PostJsonAsync($"/api/payments/{partial.Id}/void", new { reason = "Bounced cheque" }, admin.AccessToken))
            .ReadAsync<PaymentResponse>();
        voided.VoidedAt.ShouldNotBeNull();
        (await InvoiceAsync(invoice.Id, admin.AccessToken)).Status.ShouldBe(InvoiceStatus.Partial);
        await (await Client.PostJsonAsync($"/api/payments/{partial.Id}/void", new { reason = "Again" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PaymentAlreadyVoided);

        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!.Billing;
        money.Invoiced.ShouldBe(3000m);
        money.Received.ShouldBe(2000m);
        money.Outstanding.ShouldBe(1000m);
        money.NotYetInvoiced.ShouldBe(7000m);
        money.Owed.ShouldBe(8000m);

        var live = await (await Client.GetAsync($"/api/payments?projectId={project.Id}", admin.AccessToken)).ReadAsync<PagedResponse<PaymentResponse>>();
        live.Items.Count.ShouldBe(1);
        var all = await (await Client.GetAsync($"/api/payments?projectId={project.Id}&includeVoided=true", admin.AccessToken)).ReadAsync<PagedResponse<PaymentResponse>>();
        all.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Draft_and_foreign_invoices_take_no_payments()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);

        var draft = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Stage", amount = 100m } } }, superAdmin.AccessToken)).ReadAsync<InvoiceResponse>();
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 10m, currency = "USD", invoiceId = draft.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceNotIssued);

        var foreign = await IssuedInvoiceAsync(other.Id, superAdmin.AccessToken, 100m);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 10m, currency = "USD", invoiceId = foreign.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 10m, currency = "USD", receivedOn = tomorrow }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ReceivedDateInFuture);

        var internalProject = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Internal", billingType = "internal", domainCode = DomainCodes.Design }, superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        await (await Client.PostJsonAsync($"/api/projects/{internalProject.Id}/payments", new { amount = 10m, currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectNotBillable);
    }

    [Fact]
    public async Task Lebanese_pound_payments_use_the_dated_rate_or_the_one_typed_in()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 895_000m, currency = "LBP" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);

        await using (var connection = await api.Database.OpenConnectionAsync())
        {
            await using var insert = new MySqlConnector.MySqlCommand(
                "INSERT INTO exchange_rates (currency, rate_per_usd, effective_from, created_at) VALUES " +
                "('LBP', 89000, '2026-01-01', UTC_TIMESTAMP()), ('LBP', 89500, '2026-06-01', UTC_TIMESTAMP())", connection);
            await insert.ExecuteNonQueryAsync();
        }

        var dated = await RecordAsync(project.Id, superAdmin.AccessToken, new { amount = 890_000m, currency = "lbp", receivedOn = "2026-03-15" });
        dated.FxRate.ShouldBe(89_000m);
        dated.AmountUsd.ShouldBe(10m);
        dated.Currency.ShouldBe("LBP");

        var typed = await RecordAsync(project.Id, superAdmin.AccessToken, new { amount = 900_000m, currency = "LBP", fxRate = 90_000m, receivedOn = "2026-03-15" });
        typed.AmountUsd.ShouldBe(10m);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 100m, currency = "LBP", fxRate = 89_500m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PaymentTooSmall);
    }

    [Fact]
    public async Task Execution_sites_take_one_initial_payment_when_created()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);

        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa",
            clientId = client.Id,
            projectFee = 1000m,
            materialMarkup = 0.1m,
            initialPayment = new { amount = 1500m, currency = "USD", method = "cheque", reference = "CHQ-9" },
        }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var site = await created.ReadAsync<ProjectResponse>();
        site.Money!.Billing.Received.ShouldBe(1500m);
        site.Money.Billing.PaidAhead.ShouldBe(500m);
        site.Money.Billing.Owed.ShouldBe(0m);

        var initial = (await (await Client.GetAsync($"/api/payments?projectId={site.Id}", admin.AccessToken))
            .ReadAsync<PagedResponse<PaymentResponse>>()).Items.Single();
        initial.IsInitial.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 5m, currency = "USD", isInitial = true }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InitialPaymentExists);

        // Voiding it frees the slot for a corrected one.
        await Client.PostJsonAsync($"/api/payments/{initial.Id}/void", new { reason = "Wrong amount" }, admin.AccessToken);
        (await RecordAsync(site.Id, admin.AccessToken, new { amount = 1400m, currency = "USD", isInitial = true })).IsInitial.ShouldBeTrue();

        var designClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        await (await Client.PostJsonAsync("/api/projects", new
        {
            title = "Office",
            domainCode = DomainCodes.Design,
            clientId = designClient.Id,
            initialPayment = new { amount = 100m, currency = "USD" },
        }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_reach_payments()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var payment = await RecordAsync(project.Id, superAdmin.AccessToken, new { amount = 10m, currency = "USD" });

        await (await Client.GetAsync("/api/payments", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 1m, currency = "USD" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await (await Client.GetAsync($"/api/payments/{payment.Id}", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/payments/{payment.Id}/void", new { reason = "X" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 1m, currency = "USD" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
