using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The Execution box is shared by every test that pays an Execution site, so these tests follow their own projects'
/// entries and rows rather than the balance.
/// </summary>
public class BoxTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    private Task<PaymentResponse> PayAsync(long projectId, decimal amount, AuthResponse by)
        => OkAsync<PaymentResponse>(Client.PostJsonAsync($"/api/projects/{projectId}/payments", new { amount, currency = "USD" }, by.AccessToken));

    private async Task<IReadOnlyList<(BoxEntryKind Kind, decimal Amount)>> EntriesAsync(long projectId, AuthResponse by)
        => (await OkAsync<PagedResponse<BoxEntryResponse>>(Client.GetAsync($"/api/execution/box/entries?projectId={projectId}&pageSize=50", by.AccessToken)))
            .Items.Reverse().Select(e => (e.Kind, e.Amount)).ToList();

    private Task<BoxResponse> BoxAsync(AuthResponse by) => OkAsync<BoxResponse>(Client.GetAsync("/api/execution/box", by.AccessToken));

    private async Task<BoxState?> StateAsync(long projectId, AuthResponse by)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", by.AccessToken))).Money!.BoxState;

    [Fact]
    public async Task Payments_of_open_sites_go_into_the_box_and_a_void_gives_them_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await StateAsync(site.Id, money)).ShouldBe(BoxState.In);

        var payment = await PayAsync(site.Id, 1500m, superAdmin);
        (await EntriesAsync(site.Id, money)).ShouldBe([(BoxEntryKind.PaymentIn, 1500m)]);
        var row = (await BoxAsync(money)).Contributing.Single(p => p.Id == site.Id);
        row.ShouldSatisfyAllConditions(r => r.Contributed.ShouldBe(1500m), r => r.FromBox.ShouldBe(0m), r => r.BoxState.ShouldBe(BoxState.In));

        await OkAsync<PaymentResponse>(Client.PostJsonAsync($"/api/payments/{payment.Id}/void", new { reason = "Bounced" }, superAdmin.AccessToken));
        (await EntriesAsync(site.Id, money)).ShouldBe([(BoxEntryKind.PaymentIn, 1500m), (BoxEntryKind.PaymentVoided, -1500m)]);
        (await BoxAsync(money)).Contributing.Single(p => p.Id == site.Id).Contributed.ShouldBe(0m);

        // a Design project has nothing to do with the box
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await PayAsync(design.Id, 200m, superAdmin);
        (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{design.Id}", superAdmin.AccessToken))).Money!.BoxState.ShouldBeNull();
        (await BoxAsync(money)).Contributing.ShouldNotContain(p => p.Id == design.Id);
    }

    [Fact]
    public async Task Money_moves_to_a_site_in_the_red_up_to_zero_and_counts_in_its_cash_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var paying = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await PayAsync(paying.Id, 1000m, superAdmin);
        var losing = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await Client.PostJsonAsync($"/api/projects/{losing.Id}/materials", new { items = new[] { new { name = "Cement", amount = 300m } } }, superAdmin.AccessToken);

        var red = (await BoxAsync(money)).InTheRed.Single(p => p.Id == losing.Id);
        red.CashProfit.ShouldBe(-300m);

        var url = "/api/execution/box/transfers";
        await (await Client.PostJsonAsync(url, new { projectId = losing.Id, amount = 400m }, money.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync(url, new { projectId = paying.Id, amount = 10m }, money.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoxProjectNotInRed);
        await (await Client.PostJsonAsync(url, new { projectId = losing.Id, amount = 0m }, money.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var box = await OkAsync<BoxResponse>(Client.PostJsonAsync(url, new { projectId = losing.Id, amount = 250m, note = "  Cement run  " }, money.AccessToken));
        box.InTheRed.Single(p => p.Id == losing.Id).ShouldSatisfyAllConditions(p => p.FromBox.ShouldBe(250m), p => p.CashProfit.ShouldBe(-50m));

        var site = await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{losing.Id}", money.AccessToken));
        site.Money!.ShouldSatisfyAllConditions(m => m.FromBox.ShouldBe(250m), m => m.CashProfit.ShouldBe(-50m));
        var moved = (await OkAsync<PagedResponse<BoxEntryResponse>>(Client.GetAsync($"/api/execution/box/entries?projectId={losing.Id}", money.AccessToken))).Items.Single();
        moved.ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(BoxEntryKind.Transfer),
            e => e.Amount.ShouldBe(-250m),
            e => e.Note.ShouldBe("Cement run"),
            e => e.By!.Id.ShouldBe(money.User.Id));
    }

    [Fact]
    public async Task Only_whoever_sees_execution_money_reaches_the_box()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.GetAsync("/api/execution/box", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/execution/box/transfers", new { projectId = site.Id, amount = 5m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/execution/box", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/execution/box/entries", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_completed_site_is_removed_or_kept_and_a_removed_one_reopens_with_its_revenue_or_afresh()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);

        var removed = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await PayAsync(removed.Id, 800m, superAdmin);
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{removed.Id}/complete?box=remove", money.AccessToken));
        (await StateAsync(removed.Id, money)).ShouldBe(BoxState.Removed);
        (await BoxAsync(money)).Contributing.ShouldNotContain(p => p.Id == removed.Id);
        // a late payment stays out
        await PayAsync(removed.Id, 100m, superAdmin);
        (await EntriesAsync(removed.Id, money)).ShouldBe([(BoxEntryKind.PaymentIn, 800m), (BoxEntryKind.Removed, -800m)]);

        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{removed.Id}/reopen?box=readd", money.AccessToken));
        (await StateAsync(removed.Id, money)).ShouldBe(BoxState.In);
        (await EntriesAsync(removed.Id, money)).ShouldBe([(BoxEntryKind.PaymentIn, 800m), (BoxEntryKind.Removed, -800m), (BoxEntryKind.Readded, 800m)]);

        // removed again, then reopened afresh: nothing comes back, and new payments go in
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{removed.Id}/archive?box=remove", money.AccessToken));
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{removed.Id}/reopen?box=fresh", money.AccessToken));
        await PayAsync(removed.Id, 50m, superAdmin);
        (await EntriesAsync(removed.Id, money)).Skip(3).ShouldBe([(BoxEntryKind.Removed, -800m), (BoxEntryKind.PaymentIn, 50m)]);

        var kept = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await PayAsync(kept.Id, 500m, superAdmin);
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{kept.Id}/complete?box=keep", money.AccessToken));
        (await StateAsync(kept.Id, money)).ShouldBe(BoxState.In);
        await PayAsync(kept.Id, 120m, superAdmin);
        (await BoxAsync(money)).Contributing.Single(p => p.Id == kept.Id).Contributed.ShouldBe(620m);

        await (await Client.PostEmptyAsync($"/api/projects/{kept.Id}/reopen?box=keep", money.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_site_closed_by_someone_who_cannot_see_money_waits_for_a_decision_on_the_box_page()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await PayAsync(site.Id, 300m, superAdmin);

        await (await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete?box=remove", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", admin.AccessToken));
        (await StateAsync(site.Id, money)).ShouldBe(BoxState.Pending);
        (await BoxAsync(money)).Pending.Single(p => p.Id == site.Id).Contributed.ShouldBe(300m);

        // its payments keep going in while it waits, and leave with it
        await PayAsync(site.Id, 40m, superAdmin);
        var decision = $"/api/execution/box/projects/{site.Id}/decision";
        await (await Client.PostJsonAsync(decision, new { decision = "remove" }, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync(decision, new { decision = "fresh" }, money.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var box = await OkAsync<BoxResponse>(Client.PostJsonAsync(decision, new { decision = "remove" }, money.AccessToken));
        box.Pending.ShouldNotContain(p => p.Id == site.Id);
        (await EntriesAsync(site.Id, money)).ShouldBe([(BoxEntryKind.PaymentIn, 300m), (BoxEntryKind.PaymentIn, 40m), (BoxEntryKind.Removed, -340m)]);
        await (await Client.PostJsonAsync(decision, new { decision = "keep" }, money.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoxNoDecision);

        // a pending site that is kept stays in
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{other.Id}/complete", admin.AccessToken));
        await OkAsync<BoxResponse>(Client.PostJsonAsync($"/api/execution/box/projects/{other.Id}/decision", new { decision = "keep" }, money.AccessToken));
        (await StateAsync(other.Id, money)).ShouldBe(BoxState.In);
    }
}
