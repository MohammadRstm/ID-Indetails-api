using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// An Execution site's account figures: price, payments, costs, supervision taken and the cash that should be in the project
/// account, split by label, category and payee. Every site here is its own, so the class's shared database never mixes figures.
/// </summary>
public class AccountFiguresTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private BoqTestSteps Boqs => new(_steps);

    private static DateOnly CompanyToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut")));

    private static string Day(int daysAgo) => CompanyToday().AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    /// <summary>
    /// An Execution site at 10% supervision and 11% TVA (unless said), priced by an agreed client BOQ of
    /// <paramref name="work"/> (none when 0).
    /// </summary>
    private async Task<ProjectResponse> SiteAsync(AuthResponse superAdmin, decimal work, decimal supervisionRate = 0.10m, decimal tvaRate = 0.11m)
    {
        var token = superAdmin.AccessToken;
        var client = await Projects.CreateClientAsync(token, DomainCodes.Execution);
        var response = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Site " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Execution,
            clientId = client.Id,
            supervisionRate,
            tvaRate,
            staffIds = new[] { await Projects.SiteStaffAsync() },
        }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var site = await response.ReadAsync<ProjectResponse>();
        if (work > 0)
        {
            await Boqs.AgreeClientBoqAsync(token, site.Id, work);
        }

        return site;
    }

    private async Task<AccountLabelResponse> AddLabelAsync(string accessToken)
        => await OkAsync<AccountLabelResponse>(Client.PostJsonAsync("/api/account-labels", new { name = "Label " + Guid.NewGuid().ToString("N")[..8] }, accessToken));

    private async Task<long> PayAsync(long projectId, string accessToken, decimal amount, long? accountLabelId = null)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", new { amount, currency = "USD", accountLabelId }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<ID_Indetails.api.DTO.Response.Billing.PaymentResponse>()).Id;
    }

    private async Task SpendAsync(long projectId, string accessToken, params object[] items)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/site-expenses", new { items }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
    }

    private async Task TakeAsync(long projectId, string accessToken, decimal amountUsd)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/supervision-withdrawals", new { amountUsd, takenOn = Day(1) }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
    }

    private Task<AccountFiguresResponse> FiguresAsync(long projectId, string accessToken)
        => OkAsync<AccountFiguresResponse>(Client.GetAsync($"/api/projects/{projectId}/account-figures", accessToken));

    private async Task<ProjectMoneyResponse> MoneyAsync(long projectId, string accessToken)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", accessToken))).Money!;

    private async Task ReportLabourAsync(long projectId, string accessToken, int workers, decimal dailyPay)
    {
        var weeks = await OkAsync<LabourWeekListResponse>(Client.GetAsync($"/api/projects/{projectId}/labour-weeks", accessToken));
        var weekStart = weeks.Weeks[0].WeekStart;
        var week = await OkAsync<LabourWeekResponse>(Client.GetAsync($"/api/projects/{projectId}/labour-weeks/{weekStart:yyyy-MM-dd}", accessToken));
        var days = new[] { new { date = week.Days[^1].Date, workers, dailyPay } };
        await OkAsync<LabourWeekResponse>(Client.PutJsonAsync($"/api/projects/{projectId}/labour-weeks/{weekStart:yyyy-MM-dd}", new { days }, accessToken));
    }

    /// <summary>An admin of Execution given exactly <paramref name="codes"/> beyond what an admin holds.</summary>
    private async Task<AuthResponse> AdminWithAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = codes.Select(code => new { code, action = "grant", reason = "Test" }) }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }

    [Fact]
    public async Task A_site_with_a_price_labelled_payments_and_expenses_labour_a_pocket_move_and_supervision_taken_adds_up()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var concrete = await AddLabelAsync(token);
        var finishing = await AddLabelAsync(token);
        // 100,000 of work at 10% supervision and 11% TVA: 10,000 supervision, 11,000 TVA, 121,000 for the client
        var site = await SiteAsync(superAdmin, 100_000m);
        await _steps.ExecuteAsync("INSERT INTO exchange_rates (currency, rate_per_usd, effective_from, created_at) VALUES ('LBP', 89500, '2026-01-01', UTC_TIMESTAMP())");

        await PayAsync(site.Id, token, 50_000m, concrete.Id);
        await PayAsync(site.Id, token, 30_000m, finishing.Id);
        await PayAsync(site.Id, token, 20_000m);
        var voided = await PayAsync(site.Id, token, 5_000m, concrete.Id);
        (await Client.PostJsonAsync($"/api/payments/{voided}/void", new { reason = "Entered twice" }, token)).IsSuccessStatusCode.ShouldBeTrue();

        await SpendAsync(site.Id, token,
            new { name = "Ready-mix", amount = 20_000m, category = "materials", supplier = "Beirut Concrete", accountLabelId = concrete.Id },
            new { name = "Formwork crew", amount = 8_000m, category = "contractor", supplier = "Al Nour", accountLabelId = concrete.Id },
            new { name = "Tiles", amount = 6_000m, category = "materials", supplier = "Tile House", accountLabelId = finishing.Id },
            new { name = "Permit fee", amount = 1_500m, category = "fees" },
            new { name = "Sand", amount = 500m, category = "materials", supplier = "  Beirut Concrete" },
            // 8,950,000 LBP at 89,500 on a day with a rate: 100 USD
            new { name = "Grout", amount = 8_950_000m, currency = "LBP", spentOn = "2026-03-10", category = "materials", supplier = "Tile House", accountLabelId = finishing.Id },
            // no rate that day: left out of every sum, and listed apart
            new { name = "Old invoice", amount = 1_000_000m, currency = "LBP", spentOn = "2025-12-31", category = "other" });
        await ReportLabourAsync(site.Id, token, workers: 5, dailyPay: 40m);

        // 3,000 set aside in the pocket money, and 500 paid back
        await OkAsync<PocketResponse>(Client.PostJsonAsync("/api/execution/pocket/contributions", new { projectId = site.Id, amount = 3_000m }, token));
        var request = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync("/api/execution/pocket/requests", new { projectId = site.Id }, token));
        await OkAsync<PocketRequestResponse>(Client.PostJsonAsync($"/api/execution/pocket/requests/{request.Id}/approve", new { amount = 500m }, token));

        // 4,000 of the supervision taken; a second 1,500 taken and removed again
        await TakeAsync(site.Id, token, 4_000m);
        await TakeAsync(site.Id, token, 1_500m);
        var list = await OkAsync<List<SupervisionWithdrawalResponse>>(Client.GetAsync($"/api/projects/{site.Id}/supervision-withdrawals", token));
        (await Client.DeleteAsync($"/api/supervision-withdrawals/{list.Single(w => w.AmountUsd == 1_500m).Id}", token)).IsSuccessStatusCode.ShouldBeTrue();

        var figures = await FiguresAsync(site.Id, token);

        figures.ShouldSatisfyAllConditions(
            f => f.WorkUsd.ShouldBe(100_000m),
            f => f.SupervisionRate.ShouldBe(0.10m),
            f => f.SupervisionUsd.ShouldBe(10_000m),
            f => f.TvaRate.ShouldBe(0.11m),
            f => f.TvaUsd.ShouldBe(11_000m),
            f => f.ClientTotalUsd.ShouldBe(121_000m),
            // the voided payment is not counted
            f => f.ReceivedUsd.ShouldBe(100_000m),
            f => f.RestToPayUsd.ShouldBe(21_000m),
            f => f.Supervision.ShouldBe(new AccountSupervisionResponse(10_000m, 4_000m, 6_000m)),
            f => f.Pocket.ShouldBe(new AccountPocketResponse(3_000m, 500m)),
            // 100,000 − 36,300 − 4,000 − 3,000 + 500
            f => f.CashInAccountUsd.ShouldBe(57_200m));

        // the costs and the profit are the project's own
        figures.Costs.ShouldBe(new AccountCostsResponse(36_100m, 200m, 0m, 0m, 36_300m));
        var money = await MoneyAsync(site.Id, token);
        money.Cost.SiteExpenses.ShouldBe(figures.Costs.SiteExpensesUsd);
        money.Cost.GroundLabour.ShouldBe(figures.Costs.GroundLabourUsd);
        money.Cost.Labour.ShouldBe(figures.Costs.StaffUsd);
        money.Cost.Expenses.ShouldBe(figures.Costs.OtherUsd);
        money.Cost.Total.ShouldBe(figures.Costs.TotalUsd);
        figures.ProfitUsd.ShouldBe(money.Profit);
        // 110,000 agreed − 36,300 + 500 paid back − 3,000 moved
        figures.ProfitUsd.ShouldBe(71_200m);
        figures.ClientTotalUsd.ShouldBe(money.RevenueWithTva);
        figures.SupervisionUsd.ShouldBe(money.SupervisionUsd!.Value);

        // labels in their order, then the unlabelled row that carries the rest
        figures.ByLabel.Count.ShouldBe(3);
        figures.ByLabel[0].ShouldSatisfyAllConditions(
            r => r.AccountLabel!.Id.ShouldBe(concrete.Id),
            r => r.AccountLabel!.Name.ShouldBe(concrete.Name),
            r => r.ReceivedUsd.ShouldBe(50_000m),
            r => r.SpentUsd.ShouldBe(28_000m),
            r => r.OtherUsd.ShouldBe(0m),
            r => r.RestUsd.ShouldBe(22_000m));
        figures.ByLabel[1].ShouldSatisfyAllConditions(
            r => r.AccountLabel!.Id.ShouldBe(finishing.Id),
            r => r.ReceivedUsd.ShouldBe(30_000m),
            r => r.SpentUsd.ShouldBe(6_100m),
            r => r.OtherUsd.ShouldBe(0m),
            r => r.RestUsd.ShouldBe(23_900m));
        figures.ByLabel[2].ShouldSatisfyAllConditions(
            r => r.AccountLabel.ShouldBeNull(),
            r => r.ReceivedUsd.ShouldBe(20_000m),
            // the permit fee and the sand, and the ground labour
            r => r.SpentUsd.ShouldBe(2_200m),
            // supervision taken and the pocket money's net move
            r => r.OtherUsd.ShouldBe(6_500m),
            r => r.RestUsd.ShouldBe(11_300m));
        figures.ByLabel.Sum(r => r.RestUsd).ShouldBe(figures.CashInAccountUsd);
        figures.ByLabel.Sum(r => r.ReceivedUsd).ShouldBe(figures.ReceivedUsd);
        figures.ByLabel.Sum(r => r.SpentUsd).ShouldBe(figures.Costs.TotalUsd);

        // site expenses only, largest first, those with a rate
        figures.ByCategory.Select(c => (c.Category, c.SpentUsd)).ShouldBe(
            [(SiteExpenseCategory.Materials, 26_600m), (SiteExpenseCategory.Contractor, 8_000m), (SiteExpenseCategory.Fees, 1_500m)]);
        figures.ByPayee.Select(p => (p.Payee, p.SpentUsd)).ShouldBe(
            [("Beirut Concrete", 20_500m), ("Al Nour", 8_000m), ("Tile House", 6_100m), ((string?)null, 1_500m)]);
        figures.ByCategory.Sum(c => c.SpentUsd).ShouldBe(figures.Costs.SiteExpensesUsd);

        // the expense with no rate that day
        figures.Unconverted.Select(u => (u.Source, u.Currency, u.Amount)).ShouldBe([(CostSource.SiteExpense, "LBP", 1_000_000m)]);
        money.Cost.Unconverted.Select(u => (u.Source, u.Currency, u.Amount)).ShouldBe(figures.Unconverted.Select(u => (u.Source, u.Currency, u.Amount)));
    }

    [Fact]
    public async Task The_response_has_exactly_the_fields_the_client_reads()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var label = await AddLabelAsync(token);
        var site = await SiteAsync(superAdmin, 1_000m);
        await PayAsync(site.Id, token, 100m, label.Id);
        await SpendAsync(site.Id, token,
            new { name = "Tiles", amount = 10m, supplier = "Tile House", accountLabelId = label.Id },
            new { name = "Unrated", amount = 5m, currency = "LBP", spentOn = "2001-01-01" });

        var json = JsonDocument.Parse(await (await Client.GetAsync($"/api/projects/{site.Id}/account-figures", token)).Content.ReadAsStringAsync()).RootElement;
        IEnumerable<string> Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name);

        Names(json).ShouldBe(
        [
            "workUsd", "supervisionRate", "supervisionUsd", "tvaRate", "tvaUsd", "clientTotalUsd", "receivedUsd", "restToPayUsd", "costs",
            "profitUsd", "supervision", "pocket", "cashInAccountUsd", "byLabel", "byCategory", "byPayee", "unconverted",
        ]);
        Names(json.GetProperty("costs")).ShouldBe(["siteExpensesUsd", "groundLabourUsd", "staffUsd", "otherUsd", "totalUsd"]);
        Names(json.GetProperty("supervision")).ShouldBe(["dueUsd", "takenUsd", "leftUsd"]);
        Names(json.GetProperty("pocket")).ShouldBe(["movedToPocketUsd", "paidByPocketUsd"]);

        var rows = json.GetProperty("byLabel").EnumerateArray().ToList();
        Names(rows[0]).ShouldBe(["accountLabel", "receivedUsd", "spentUsd", "otherUsd", "restUsd"]);
        Names(rows[0].GetProperty("accountLabel")).ShouldBe(["id", "name"]);
        rows[^1].GetProperty("accountLabel").ValueKind.ShouldBe(JsonValueKind.Null);

        var category = json.GetProperty("byCategory")[0];
        Names(category).ShouldBe(["category", "spentUsd"]);
        category.GetProperty("category").GetString().ShouldBe("materials");
        Names(json.GetProperty("byPayee")[0]).ShouldBe(["payee", "spentUsd"]);

        var unconverted = json.GetProperty("unconverted")[0];
        Names(unconverted).ShouldBe(["source", "currency", "amount"]);
        unconverted.GetProperty("source").GetString().ShouldBe("site_expense");
    }

    [Fact]
    public async Task A_site_with_nothing_recorded_has_zero_figures_and_one_unlabelled_row()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await SiteAsync(superAdmin, work: 0);

        var figures = await FiguresAsync(site.Id, superAdmin.AccessToken);

        figures.ShouldSatisfyAllConditions(
            f => f.WorkUsd.ShouldBe(0m),
            f => f.SupervisionRate.ShouldBe(0.10m),
            f => f.SupervisionUsd.ShouldBe(0m),
            f => f.TvaUsd.ShouldBe(0m),
            f => f.ClientTotalUsd.ShouldBe(0m),
            f => f.ReceivedUsd.ShouldBe(0m),
            f => f.RestToPayUsd.ShouldBe(0m),
            f => f.Costs.TotalUsd.ShouldBe(0m),
            f => f.ProfitUsd.ShouldBe(0m),
            f => f.Supervision.ShouldBe(new AccountSupervisionResponse(0m, 0m, 0m)),
            f => f.Pocket.ShouldBe(new AccountPocketResponse(0m, 0m)),
            f => f.CashInAccountUsd.ShouldBe(0m),
            f => f.ByCategory.ShouldBeEmpty(),
            f => f.ByPayee.ShouldBeEmpty(),
            f => f.Unconverted.ShouldBeEmpty());
        var only = figures.ByLabel.ShouldHaveSingleItem();
        only.AccountLabel.ShouldBeNull();
        (only.ReceivedUsd, only.SpentUsd, only.OtherUsd, only.RestUsd).ShouldBe((0m, 0m, 0m, 0m));
    }

    [Fact]
    public async Task Paying_ahead_and_taking_more_supervision_than_is_due_show_as_negative_and_supervision_never_moves_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        // 1,000 of work and 10% supervision, no TVA: 1,100 for the client, 100 of supervision due
        var site = await SiteAsync(superAdmin, 1_000m, tvaRate: 0m);
        await PayAsync(site.Id, token, 1_200m);

        var paidAhead = await FiguresAsync(site.Id, token);
        paidAhead.ShouldSatisfyAllConditions(
            f => f.ClientTotalUsd.ShouldBe(1_100m),
            f => f.RestToPayUsd.ShouldBe(-100m),
            f => f.CashInAccountUsd.ShouldBe(1_200m),
            f => f.Supervision.LeftUsd.ShouldBe(100m));
        var profit = (await MoneyAsync(site.Id, token)).Profit;

        // allowed: taking 150 of the 100 due
        await TakeAsync(site.Id, token, 150m);

        var over = await FiguresAsync(site.Id, token);
        over.ShouldSatisfyAllConditions(
            f => f.Supervision.ShouldBe(new AccountSupervisionResponse(100m, 150m, -50m)),
            f => f.CashInAccountUsd.ShouldBe(1_050m),
            f => f.ProfitUsd.ShouldBe(profit),
            f => f.Costs.TotalUsd.ShouldBe(0m));
        over.ByLabel.Single().OtherUsd.ShouldBe(150m);
        (await MoneyAsync(site.Id, token)).Profit.ShouldBe(profit);
    }

    [Fact]
    public async Task Relabelling_a_payment_moves_it_between_rows_and_a_label_archived_after_use_keeps_its_row()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var first = await AddLabelAsync(token);
        var second = await AddLabelAsync(token);
        var site = await SiteAsync(superAdmin, 1_000m);
        var payment = await PayAsync(site.Id, token, 400m, first.Id);
        await PayAsync(site.Id, token, 100m);

        (await FiguresAsync(site.Id, token)).ByLabel.Select(r => (r.AccountLabel?.Id, r.ReceivedUsd)).ShouldBe([((long?)first.Id, 400m), (null, 100m)]);

        await OkAsync<ID_Indetails.api.DTO.Response.Billing.PaymentResponse>(Client.PutJsonAsync($"/api/payments/{payment}/label", new { accountLabelId = second.Id }, token));
        (await FiguresAsync(site.Id, token)).ByLabel.Select(r => (r.AccountLabel?.Id, r.ReceivedUsd)).ShouldBe([((long?)second.Id, 400m), (null, 100m)]);

        // archived, the label still has its row by name
        (await Client.PostEmptyAsync($"/api/account-labels/{second.Id}/archive", token)).IsSuccessStatusCode.ShouldBeTrue();
        var archived = await FiguresAsync(site.Id, token);
        archived.ByLabel[0].AccountLabel!.Name.ShouldBe(second.Name);
        archived.ByLabel[0].ReceivedUsd.ShouldBe(400m);

        await OkAsync<ID_Indetails.api.DTO.Response.Billing.PaymentResponse>(Client.PutJsonAsync($"/api/payments/{payment}/label", new { accountLabelId = (long?)null }, token));
        var cleared = await FiguresAsync(site.Id, token);
        cleared.ByLabel.ShouldHaveSingleItem().ReceivedUsd.ShouldBe(500m);
        cleared.CashInAccountUsd.ShouldBe(500m);
    }

    [Fact]
    public async Task The_figures_need_to_see_money_and_an_execution_site_the_caller_can_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var recorder = await AdminWithAsync(superAdmin, PermissionCodes.PaymentRecord);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await SiteAsync(superAdmin, 1_000m);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var url = $"/api/projects/{site.Id}/account-figures";

        (await FiguresAsync(site.Id, viewer.AccessToken)).WorkUsd.ShouldBe(1_000m);

        // recording payments is not seeing money
        await (await Client.GetAsync(url, recorder.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(url, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(url)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);

        // a domain the caller can't see reads as missing, a Design project the caller sees has no account
        await (await Client.GetAsync(url, designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/projects/999999999/account-figures", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{design.Id}/account-figures", designMoney.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.GetAsync($"/api/projects/{design.Id}/account-figures", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }
}
