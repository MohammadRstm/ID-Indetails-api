using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>TVA: on what the client pays and owes, never on revenue or profit; each payment keeps its TVA share.</summary>
public class TvaTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<ProjectMoneyResponse> MoneyAsync(long projectId, string token)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", token))).Money!;

    private Task<decimal> TvaOfPaymentAsync(long paymentId)
        => _steps.ScalarAsync<decimal>("SELECT tva_usd FROM payments WHERE id = @id", ("@id", paymentId));

    [Fact]
    public async Task A_new_client_project_carries_15_percent_on_what_the_client_owes_never_on_its_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var project = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Villa interiors", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 10_000m }, superAdmin.AccessToken));

        var money = project.Money!;
        money.ShouldSatisfyAllConditions(
            m => m.TvaRate.ShouldBe(0.15m),
            m => m.Revenue.ShouldBe(10_000m),
            m => m.PaymentWithTva.ShouldBe(11_500m),
            m => m.RevenueWithTva.ShouldBe(11_500m),
            m => m.Profit.ShouldBe(10_000m),
            m => m.Billing.Owed.ShouldBe(11_500m),
            m => m.Billing.NotYetInvoiced.ShouldBe(11_500m));

        var payment = await OkAsync<ID_Indetails.api.DTO.Response.Billing.PaymentResponse>(
            Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 1150m, currency = "USD" }, superAdmin.AccessToken));
        (await TvaOfPaymentAsync(payment.Id)).ShouldBe(150m);
        (await MoneyAsync(project.Id, superAdmin.AccessToken)).ShouldSatisfyAllConditions(
            m => m.Billing.Received.ShouldBe(1150m),
            m => m.Billing.Owed.ShouldBe(10_350m),
            m => m.Profit.ShouldBe(10_000m));

        var today = DateTime.UtcNow.Date;
        var summary = await OkAsync<FinanceSummaryResponse>(Client.GetAsync(
            $"/api/finance/design/summary?from={today.AddDays(-2):yyyy-MM-dd}&to={today.AddDays(2):yyyy-MM-dd}", superAdmin.AccessToken));
        summary.TvaCollected.ShouldBeGreaterThanOrEqualTo(150m);
    }

    [Fact]
    public async Task The_rate_changes_on_its_own_with_a_reason_and_joins_the_payment_history()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var project = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Office fit-out", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 2000m, tvaRate = 0.15m }, superAdmin.AccessToken));
        var url = $"/api/projects/{project.Id}/payment";

        var changed = await OkAsync<ProjectResponse>(Client.PutJsonAsync(url, new { revenue = 2000m, tvaRate = 0.11m, description = "The client is taxed less" }, superAdmin.AccessToken));
        changed.Money!.ShouldSatisfyAllConditions(m => m.TvaRate.ShouldBe(0.11m), m => m.PaymentWithTva.ShouldBe(2220m));
        var history = await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{project.Id}/payment-changes", superAdmin.AccessToken));
        history.ShouldContain(h => h.TvaRate == 0.11m && h.PreviousTvaRate == 0.15m && h.Description == "The client is taxed less");
        history.ShouldContain(h => h.TvaRate == 0.15m && h.PreviousTvaRate == null);

        await (await Client.PutJsonAsync(url, new { revenue = 2000m, tvaRate = 0.11m, description = "Again" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(url, new { revenue = 2000m, tvaRate = 1.5m, description = "Too much" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await OkAsync<ProjectResponse>(Client.PutJsonAsync(url, new { revenue = 2000m, tvaRate = 0m, description = "Not taxed" }, superAdmin.AccessToken)))
            .Money!.PaymentWithTva.ShouldBe(2000m);
    }

    [Fact]
    public async Task Only_whoever_sees_money_sets_the_rate_and_internal_work_has_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostJsonAsync("/api/projects", new { title = "Taxed", domainCode = DomainCodes.Design, clientId = client.Id, tvaRate = 0.2m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var byAdmin = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects", new { title = "Default rate", domainCode = DomainCodes.Design, clientId = client.Id }, admin.AccessToken));
        (await MoneyAsync(byAdmin.Id, superAdmin.AccessToken)).TvaRate.ShouldBe(0.15m);

        var internalWork = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Studio upkeep", domainCode = DomainCodes.Design, billingType = "internal", tvaRate = 0.15m }, superAdmin.AccessToken));
        internalWork.Money!.TvaRate.ShouldBe(0m);

        // a client project made internal stops carrying TVA
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{byAdmin.Id}/billing", new { billingType = "internal" }, superAdmin.AccessToken));
        (await MoneyAsync(byAdmin.Id, superAdmin.AccessToken)).TvaRate.ShouldBe(0m);
    }

    [Fact]
    public async Task An_execution_site_is_agreed_at_one_amount_taxed_with_its_materials_and_its_cash_profit_is_net()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var site = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects", new
        {
            title = "Harbour offices", domainCode = DomainCodes.Execution, clientId = client.Id, projectFee = 1000m, tvaRate = 0.2m,
            staffIds = new[] { await Projects.SiteStaffAsync() },
        }, superAdmin.AccessToken));
        site.Money!.ShouldSatisfyAllConditions(
            m => m.MaterialMarkup.ShouldBe(0m),
            m => m.PaymentWithTva.ShouldBe(1200m),
            m => m.Billing.Owed.ShouldBe(1200m));

        var payment = await OkAsync<ID_Indetails.api.DTO.Response.Billing.PaymentResponse>(
            Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 600m, currency = "USD" }, superAdmin.AccessToken));
        (await TvaOfPaymentAsync(payment.Id)).ShouldBe(100m);
        (await MoneyAsync(site.Id, superAdmin.AccessToken)).ShouldSatisfyAllConditions(
            m => m.Billing.Received.ShouldBe(600m),
            m => m.CashProfit.ShouldBe(500m));

        // the rate changes with the agreed amount, on the pricing
        var priced = await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { projectFee = 1000m, materialMarkup = 0m, tvaRate = 0.1m, description = "Lower rate for this client" }, superAdmin.AccessToken));
        priced.Money!.PaymentWithTva.ShouldBe(1100m);
    }
}
