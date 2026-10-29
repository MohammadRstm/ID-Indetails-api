using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The pocket money is shared by every test that moves money with it, so these tests follow their own sites' entries
/// and requests rather than the balance.
/// </summary>
public class PocketTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    private Task<HttpResponseMessage> ContributeAsync(long projectId, decimal amount, AuthResponse by, string? note = null)
        => Client.PostJsonAsync("/api/execution/pocket/contributions", new { projectId, amount, note }, by.AccessToken);

    private async Task<ProjectMoneyResponse> MoneyAsync(long projectId, AuthResponse by)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", by.AccessToken))).Money!;

    private async Task<IReadOnlyList<PocketEntryResponse>> EntriesAsync(string query, AuthResponse by)
        => (await OkAsync<PagedResponse<PocketEntryResponse>>(Client.GetAsync($"/api/execution/pocket/entries?{query}", by.AccessToken))).Items;

    /// <summary>A site agreed at 1,000 USD whose expense leaves it <paramref name="profit"/> of profit.</summary>
    private async Task<ProjectResponse> SiteWithProfitAsync(AuthResponse superAdmin, decimal profit)
    {
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{site.Id}/expenses",
            new { label = "Scaffolding", amount = 1000m - profit, currency = "USD", spentOn = DateTime.UtcNow.ToString("yyyy-MM-dd") }, superAdmin.AccessToken));
        return site;
    }

    [Fact]
    public async Task A_super_admin_moves_part_of_a_sites_money_into_the_pocket_never_all_of_its_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await MoneyAsync(site.Id, money)).Profit.ShouldBe(1000m);

        await (await ContributeAsync(site.Id, 1000m, money)).ShouldFailWithAsync(422, ErrorCodes.PocketWouldEmptySite);
        await (await ContributeAsync(site.Id, 0m, money)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await OkAsync<PocketResponse>(ContributeAsync(site.Id, 400m, money, "  Set aside for the winter  "));

        var after = await MoneyAsync(site.Id, money);
        after.ShouldSatisfyAllConditions(
            m => m.ToPocket.ShouldBe(400m),
            m => m.FromPocket.ShouldBe(0m),
            m => m.Profit.ShouldBe(600m),
            // what the client owes doesn't change
            m => m.Revenue.ShouldBe(1000m),
            m => m.CashProfit.ShouldBe(-400m));
        var entry = (await EntriesAsync($"projectId={site.Id}", money)).Single();
        entry.ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(PocketEntryKind.Contribution),
            e => e.Amount.ShouldBe(400m),
            e => e.Note.ShouldBe("Set aside for the winter"),
            e => e.By.Id.ShouldBe(money.User.Id));

        // a Design project has no pocket money
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await (await ContributeAsync(design.Id, 10m, money)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_site_asks_for_money_the_system_suggests_how_much_and_a_super_admin_approves_their_own_amount_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var giver = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync<PocketResponse>(ContributeAsync(giver.Id, 500m, money));
        var short_ = await SiteWithProfitAsync(superAdmin, 50m);

        var request = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync("/api/execution/pocket/requests", new { projectId = short_.Id, note = "Tiles came in dearer" }, money.AccessToken));
        request.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(PocketRequestStatus.Pending),
            // back to a 10% margin on 1,000: 100 of profit, 50 short
            r => r.SuggestedAmount.ShouldBe(50m),
            r => r.SuggestedNow.ShouldBe(50m),
            r => r.RequestedBy!.Id.ShouldBe(money.User.Id));
        // asking again gets the same request back
        (await OkAsync<PocketRequestResponse>(Client.PostJsonAsync("/api/execution/pocket/requests", new { projectId = short_.Id }, money.AccessToken)))
            .Id.ShouldBe(request.Id);
        (await OkAsync<PocketResponse>(Client.GetAsync("/api/execution/pocket", money.AccessToken))).Pending.ShouldContain(r => r.Id == request.Id);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE kind = 'pocket_requested' AND subject_id = @id AND recipient_id = @to",
            ("@id", request.Id), ("@to", superAdmin.User.Id))).ShouldBe(1);

        var approve = $"/api/execution/pocket/requests/{request.Id}/approve";
        await (await Client.PostJsonAsync(approve, new { amount = 1_000_000_000m }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PocketShort);
        var approved = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync(approve, new { amount = 80m, note = "A bit more than asked" }, superAdmin.AccessToken));
        approved.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(PocketRequestStatus.Approved),
            r => r.ApprovedAmount.ShouldBe(80m),
            r => r.DecidedBy!.Id.ShouldBe(superAdmin.User.Id),
            r => r.SuggestedNow.ShouldBeNull());
        await (await Client.PostJsonAsync(approve, new { amount = 80m }, money.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PocketRequestDecided);
        await (await Client.PostJsonAsync($"/api/execution/pocket/requests/{request.Id}/deny", new { }, money.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PocketRequestDecided);

        var payout = (await EntriesAsync($"projectId={short_.Id}&kind=payout", money)).Single();
        payout.ShouldSatisfyAllConditions(e => e.Amount.ShouldBe(-80m), e => e.RequestId.ShouldBe(request.Id));
        (await MoneyAsync(short_.Id, money)).ShouldSatisfyAllConditions(m => m.FromPocket.ShouldBe(80m), m => m.Profit.ShouldBe(130m));
    }

    [Fact]
    public async Task A_denied_request_pays_nothing_and_the_site_can_ask_again()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await SiteWithProfitAsync(superAdmin, 20m);
        var first = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync("/api/execution/pocket/requests", new { projectId = site.Id }, money.AccessToken));

        var denied = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync($"/api/execution/pocket/requests/{first.Id}/deny", new { note = "Raise the price instead" }, money.AccessToken));
        denied.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(PocketRequestStatus.Denied), r => r.ApprovedAmount.ShouldBeNull(), r => r.DecisionNote.ShouldBe("Raise the price instead"));
        (await EntriesAsync($"projectId={site.Id}", money)).ShouldBeEmpty();

        var second = await OkAsync<PocketRequestResponse>(Client.PostJsonAsync("/api/execution/pocket/requests", new { projectId = site.Id }, money.AccessToken));
        second.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task Movements_in_and_out_are_listed_apart_and_searched()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var word = Guid.NewGuid().ToString("N")[..10];
        await OkAsync<PocketResponse>(ContributeAsync(site.Id, 100m, money, $"Spare {word}"));

        (await EntriesAsync($"kind=contribution&search={word}", money)).Single().Amount.ShouldBe(100m);
        (await EntriesAsync($"kind=payout&search={word}", money)).ShouldBeEmpty();
        (await EntriesAsync($"search={Uri.EscapeDataString(site.Title)}", money)).Count.ShouldBe(1);
        var today = DateTime.UtcNow.Date;
        (await EntriesAsync($"search={word}&from={today.AddDays(2):yyyy-MM-dd}", money)).ShouldBeEmpty();
        await (await Client.GetAsync($"/api/execution/pocket/entries?from={today:yyyy-MM-dd}&to={today.AddDays(-3):yyyy-MM-dd}", money.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Only_whoever_sees_execution_money_reaches_the_pocket()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.GetAsync("/api/execution/pocket", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await ContributeAsync(site.Id, 5m, admin)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/execution/pocket", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/execution/pocket/entries", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
