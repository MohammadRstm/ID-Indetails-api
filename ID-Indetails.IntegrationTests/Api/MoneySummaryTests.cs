using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: the domain figures add up every project of the domain.</summary>
public class MoneySummaryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task<T> OkAsync<T>(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Design_money_adds_up_across_currencies_billing_and_clients()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        // 1,733.30 a month is 10 an hour (÷ 173.33).
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        var clientId = project.Client!.Id;
        var internalWork = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling", billingType = "internal" }, admin.AccessToken)).ReadAsync<ProjectResponse>();

        // Tuesday 10 February 2026 (Beirut is UTC+2): an hour on the project (09:00-10:00) and an hour on internal work
        // (11:00-12:00), 10 each, and the paid hour of break shared between them, 5 each. Then an LBP expense converted
        // at today's rate (10), and one dated before any LBP rate.
        await Work.InsertAsync(designer.User.Id, project.Id, "2026-02-10 07:00:00", "2026-02-10 08:00:00");
        await Work.InsertAsync(designer.User.Id, internalWork.Id, "2026-02-10 09:00:00", "2026-02-10 10:00:00");
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Taxi", amount = 895_000m, currency = "LBP" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Old print run", amount = 1_790_000m, currency = "LBP", spentOn = "2025-12-15" }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // Invoiced 3,000 two months ago (45-day terms, so overdue), 1,000 paid against it and 500 on account.
        var draft = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Stage 1", amount = 3000m } } }, admin.AccessToken)).ReadAsync<InvoiceResponse>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut")));
        var issuedOn = today.AddDays(-60);
        var invoice = await (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { issuedOn = issuedOn.ToString("yyyy-MM-dd") }, admin.AccessToken))
            .ReadAsync<InvoiceResponse>();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 1000m, currency = "USD", invoiceId = invoice.Id, method = "bank" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 500m, currency = "USD", method = "cash" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        // Completed, so all 10,000 is recognised, spread over the months of its costs.
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // From December 2025, so the period holds the work, both expenses and today.
        var summary = await OkAsync<FinanceSummaryResponse>("/api/finance/design/summary?from=2025-12-01", admin.AccessToken);
        summary.Revenue.ShouldBe(10_000m);
        summary.Cost.Labour.ShouldBe(30m);
        summary.Cost.Expenses.ShouldBe(10m);
        summary.Cost.Total.ShouldBe(40m);
        summary.Profit.ShouldBe(9960m);
        summary.Unconverted.ShouldBe([new UnconvertedCostResponse(CostSource.Expenses, "LBP", 1_790_000m)]);
        summary.ClientProjects.ShouldBe(1);
        summary.InternalProjects.ShouldBe(1);
        summary.AllTimeInternalCost.ShouldBe(15m);
        summary.StillToEarn.ShouldBe(0m);
        summary.Billing.Invoiced.ShouldBe(3000m);
        summary.Billing.Received.ShouldBe(1500m);
        summary.Billing.OnAccount.ShouldBe(500m);
        summary.Billing.Outstanding.ShouldBe(2000m);
        summary.Billing.Overdue.ShouldBe(2000m);
        summary.Billing.OverdueCount.ShouldBe(1);
        summary.Billing.OldestOverdueDays.ShouldBe(15);
        summary.Billing.NotYetInvoiced.ShouldBe(7000m);
        summary.Billing.PaymentCount.ShouldBe(2);

        // The unconverted expense belongs to December: a period without it doesn't list it.
        var thisYear = await OkAsync<FinanceSummaryResponse>("/api/finance/design/summary?from=2026-01-01", admin.AccessToken);
        thisYear.Unconverted.ShouldBeEmpty();
        thisYear.Cost.Total.ShouldBe(40m);

        var projects = await OkAsync<FinanceProjectsResponse>("/api/finance/design/projects", admin.AccessToken);
        var row = projects.Projects.Single(p => p.ProjectId == project.Id);
        row.Revenue.ShouldBe(10_000m);
        row.Cost.Total.ShouldBe(25m);
        row.Profit.ShouldBe(9975m);
        row.MarginKnown.ShouldBeTrue();
        projects.Projects.Single(p => p.ProjectId == internalWork.Id).Profit.ShouldBe(-15m);
        projects.Totals.InternalCost.ShouldBe(15m);

        var months = await OkAsync<List<FinanceMonthResponse>>("/api/finance/design/monthly?months=36", admin.AccessToken);
        months.Count.ShouldBe(36);
        months.Sum(m => m.Revenue).ShouldBe(10_000m);
        months.Single(m => m.Month == new DateOnly(2026, 2, 1)).Labour.ShouldBe(30m);
        months.Single(m => m.Month == new DateOnly(2025, 12, 1)).Unconverted
            .ShouldBe([new UnconvertedCostResponse(CostSource.Expenses, "LBP", 1_790_000m)]);
        months.Where(m => m.Month != new DateOnly(2025, 12, 1)).SelectMany(m => m.Unconverted).ShouldBeEmpty();

        var billing = await OkAsync<FinanceBillingResponse>("/api/finance/design/billing", admin.AccessToken);
        billing.Aging.Single(a => a.FromDays == 1).Amount.ShouldBe(2000m);
        billing.OpenInvoices.Single().DaysOverdue.ShouldBe(15);
        billing.ByClient.Single().ClientId.ShouldBe(clientId);
        billing.ByMethod.ShouldContain(m => m.Method == PaymentMethod.Cash && m.Amount == 500m);
        billing.Monthly.Sum(m => m.Collected).ShouldBe(1500m);

        // The completed project's invoice is overdue, so it isn't also flagged as finished but owed.
        var attention = await OkAsync<List<MoneyAttentionResponse>>("/api/finance/design/attention", admin.AccessToken);
        var overdue = attention.ShouldHaveSingleItem();
        overdue.Kind.ShouldBe(MoneyAttentionKind.OverdueInvoice);
        overdue.ProjectId.ShouldBe(project.Id);
        overdue.InvoiceNumber.ShouldBe(invoice.Number);
        overdue.DaysOverdue.ShouldBe(15);
        overdue.Amount.ShouldBe(2000m);
        overdue.ClientName.ShouldNotBeNull();

        var clients = await OkAsync<List<FinanceClientResponse>>("/api/finance/design/clients", admin.AccessToken);
        var client = clients.Single(c => c.ClientId == clientId);
        client.Money.Revenue.ShouldBe(10_000m);
        client.Money.CompletedProjects.ShouldBe(1);
        client.Money.MarginKnown.ShouldBeTrue();
        client.Money.Billing.Outstanding.ShouldBe(2000m);

        var costs = await OkAsync<FinanceCostsResponse>("/api/finance/design/costs?from=2025-12-01", admin.AccessToken);
        costs.LabourByPerson.Single().Usd.ShouldBe(30m);
        costs.ExpensesByCategory.Sum(c => c.Usd).ShouldBe(10m);
        costs.Unconverted.ShouldBe([new UnconvertedCostResponse(CostSource.Expenses, "LBP", 1_790_000m)]);

        // The project page's billing uses the same rule as the Financials page.
        var detailMoney = (await OkAsync<ProjectResponse>($"/api/projects/{project.Id}", admin.AccessToken)).Money!;
        detailMoney.Cost.Labour.ShouldBe(15m);
        detailMoney.Billing.Invoiced.ShouldBe(3000m);
        detailMoney.Billing.IssuedCount.ShouldBe(1);
        detailMoney.Billing.Received.ShouldBe(1500m);
        detailMoney.Billing.PaymentCount.ShouldBe(2);
        detailMoney.Billing.Outstanding.ShouldBe(2000m);
        detailMoney.Billing.Overdue.ShouldBe(2000m);
        detailMoney.Billing.OverdueCount.ShouldBe(1);
        detailMoney.Billing.OldestOverdueDays.ShouldBe(15);
        detailMoney.Billing.Drafts.ShouldBe(0m);
        detailMoney.Billing.UnconvertedInvoices.ShouldBeEmpty();

        // The project list and the client pages carry the same money, and flag the unconverted December expense.
        var list = await OkAsync<PagedResponse<ProjectSummaryResponse>>($"/api/projects?clientId={clientId}", admin.AccessToken);
        list.Items.Single().Money!.Outstanding.ShouldBe(2000m);
        list.Items.Single().Money!.HasUnconverted.ShouldBeTrue();
        list.Items.Single().Money!.UnpricedLabourMinutes.ShouldBe(0);
        var detail = await OkAsync<ClientResponse>($"/api/clients/{clientId}", admin.AccessToken);
        detail.Money!.Profit.ShouldBe(9975m);
        detail.Money.UnconvertedInvoices.ShouldBeEmpty();
        detail.ProjectMoney!.Single().Billing.Invoiced.ShouldBe(3000m);
        detail.ProjectMoney!.Single().HasUnconverted.ShouldBeTrue();
        (await OkAsync<PagedResponse<ClientSummaryResponse>>("/api/clients", admin.AccessToken)).Items.Single(c => c.Id == clientId).Money!.Revenue
            .ShouldBe(10_000m);
    }
}

/// <summary>Separate from <see cref="MoneySummaryTests"/> so its project doesn't change that class's domain totals.</summary>
public class MoneySummaryAccessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Money_needs_financial_view_and_stays_inside_the_callers_domains()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        foreach (var path in new[] { "summary", "monthly", "projects", "billing", "clients", "costs", "attention" })
        {
            await (await Client.GetAsync($"/api/finance/design/{path}", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.GetAsync($"/api/finance/design/{path}", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        }

        (await Client.GetAsync("/api/finance/execution/summary", executionAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.GetAsync("/api/finance/company/summary", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var dashboard = await OkAsync<DashboardResponse>("/api/dashboard/execution", executionAdmin.AccessToken);
        dashboard.Domain.ShouldBe(DomainCodes.Execution);
        await (await Client.GetAsync("/api/dashboard/design", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/dashboard/design", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // Without financial:view the money fields are left out entirely, not zeroed.
        (await Client.PutJsonAsync($"/api/users/{designAdmin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.GetAsync("/api/finance/design/summary", designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        foreach (var url in new[] { "/api/projects", "/api/clients" })
        {
            var json = await (await Client.GetAsync(url, designAdmin.AccessToken)).Content.ReadAsStringAsync();
            var items = JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray().ToList();
            items.ShouldNotBeEmpty();
            items.Select(i => i.TryGetProperty("money", out _)).ShouldNotContain(true);
        }
    }
}
