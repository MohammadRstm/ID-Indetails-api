using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: the filters and sorts are checked over every client of the domain.</summary>
public class ClientListTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static string DaysAgoUtc(int days, int hour) => DateTime.UtcNow.Date.AddDays(-days).AddHours(hour).ToString("yyyy-MM-dd HH:mm:ss");

    private async Task<IReadOnlyList<ClientSummaryResponse>> ListAsync(string query, string token)
    {
        var response = await Client.GetAsync($"/api/clients?pageSize=100&{query}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<PagedResponse<ClientSummaryResponse>>()).Items;
    }

    [Fact]
    public async Task Clients_filter_and_sort_by_projects_and_work_and_the_page_shows_who_worked_for_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // A: two projects (one completed) and 5 hours of work 40 days ago. B: a project on hold, an hour 10 days ago. C: nothing.
        var first = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var a = first.Client!.Id;
        var second = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Second job", domainCode = DomainCodes.Design, clientId = a }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        var held = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var b = held.Client!.Id;
        var c = (await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design)).Id;
        await Work.InsertAsync(anna.User.Id, first.Id, DaysAgoUtc(40, 7), DaysAgoUtc(40, 11));
        await Work.InsertAsync(ben.User.Id, second.Id, DaysAgoUtc(40, 12), DaysAgoUtc(40, 13));
        await Work.InsertAsync(anna.User.Id, held.Id, DaysAgoUtc(10, 7), DaysAgoUtc(10, 8));
        (await Client.PostEmptyAsync($"/api/projects/{second.Id}/complete", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{held.Id}/hold", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await ListAsync("filter=empty", admin.AccessToken)).Select(x => x.Id).ShouldBe([c]);
        (await ListAsync("filter=active", admin.AccessToken)).Select(x => x.Id).ShouldBe([a, b], ignoreOrder: true);
        (await ListAsync("sort=projects", admin.AccessToken)).Select(x => x.Id).ShouldBe([a, b, c]);
        (await ListAsync("sort=hours", admin.AccessToken)).Select(x => x.Id).ShouldBe([a, b, c]);
        // C has no work, so it counts from when it was added (today).
        (await ListAsync("sort=activity", admin.AccessToken)).Select(x => x.Id).ShouldBe([c, b, a]);

        var rows = await ListAsync("", admin.AccessToken);
        rows.Single(x => x.Id == a).Work!.TotalMinutes.ShouldBe(300);
        rows.Single(x => x.Id == a).Work!.LastStartedAt.ShouldNotBeNull();
        rows.Single(x => x.Id == c).Work.ShouldBe(new ClientListWorkResponse(0, null));

        var detail = await (await Client.GetAsync($"/api/clients/{a}", admin.AccessToken)).ReadAsync<ClientResponse>();
        var work = detail.Work!;
        work.TotalMinutes.ShouldBe(300);
        work.EntryCount.ShouldBe(2);
        work.ContributorCount.ShouldBe(2);
        work.People.ShouldBe([new ClientPersonWorkResponse(anna.User.Id, "Test employee", 240), new ClientPersonWorkResponse(ben.User.Id, "Test employee", 60)]);
        work.Monthly.Count.ShouldBe(12);
        work.Monthly.Sum(m => m.Minutes).ShouldBe(300);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));
        work.Monthly[^1].Month.ShouldBe(new DateOnly(today.Year, today.Month, 1));
    }
}

/// <summary>Separate from <see cref="ClientListTests"/> so its clients don't change that class's orders.</summary>
public class ClientMoneyListTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<IReadOnlyList<ClientSummaryResponse>> ListAsync(string query, string token)
    {
        // A query that pages itself keeps its own page size: a repeated pageSize binds only its first value.
        var paging = query.Contains("pageSize=") ? "" : "pageSize=100&";
        var response = await Client.GetAsync($"/api/clients?{paging}{query}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<PagedResponse<ClientSummaryResponse>>()).Items;
    }

    [Fact]
    public async Task Money_sorts_and_the_owing_filter_need_financial_view()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);
        var big = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        var small = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 2_000m);
        var none = (await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design)).Id;

        // The small client is invoiced 500 and owes it.
        var draft = await (await Client.PostJsonAsync($"/api/projects/{small.Id}/invoices",
            new { lines = new[] { new { description = "Stage 1", amount = 500m } } }, admin.AccessToken)).ReadAsync<InvoiceResponse>();
        (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await ListAsync("sort=revenue", admin.AccessToken)).Select(x => x.Id).ShouldBe([big.Client!.Id, small.Client!.Id, none]);
        // either way round, by money (sorted in memory) and by projects (by the database)
        (await ListAsync("sort=revenue&reverse=true", admin.AccessToken)).Select(x => x.Id).ShouldBe([none, small.Client!.Id, big.Client!.Id]);
        (await ListAsync("sort=projects&reverse=true", admin.AccessToken)).Select(x => x.Id).First().ShouldBe(none);
        (await ListAsync("sort=projects", admin.AccessToken)).Select(x => x.Id).Last().ShouldBe(none);
        (await ListAsync("sort=outstanding", admin.AccessToken)).Select(x => x.Id).First().ShouldBe(small.Client!.Id);
        var owing = await ListAsync("filter=owing", admin.AccessToken);
        owing.ShouldHaveSingleItem().Id.ShouldBe(small.Client!.Id);
        owing[0].Money!.Billing.Outstanding.ShouldBe(500m);
        // Margins aren't known yet (nothing done, under 5% along, no hours), so they all tie and fall back to the name.
        (await ListAsync("sort=margin&pageSize=2&page=2", admin.AccessToken)).Count.ShouldBe(1);

        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.GetAsync("/api/clients?sort=revenue", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/clients?filter=owing", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await ListAsync("sort=projects", admin.AccessToken)).Count.ShouldBe(3);

        await (await Client.GetAsync("/api/clients", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/clients?sort=cost", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}

/// <summary>Its own database, so the counts are this class's clients only.</summary>
public class ClientOverviewTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task The_overview_counts_clients_their_work_this_month_s_additions_and_growth()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var busy = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var idle = (await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design)).Id;
        var gone = (await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design)).Id;
        (await Client.PostEmptyAsync($"/api/clients/{gone}/archive", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await _steps.ExecuteAsync("UPDATE clients SET created_at = '2025-11-20 10:00:00' WHERE id = @id", ("@id", idle));

        var response = await Client.GetAsync("/api/clients/overview", admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var overview = await response.ReadAsync<ClientOverviewResponse>();

        overview.Clients.ShouldBe(2);
        overview.Archived.ShouldBe(1);
        overview.WithActiveWork.ShouldBe(1);
        overview.WithoutProjects.ShouldBe(1);
        overview.AddedThisMonth.ShouldBe(1);
        overview.Newest!.Id.ShouldBeOneOf(busy.Client!.Id, gone);
        overview.Growth.Count.ShouldBe(13);
        overview.Growth[^1].Total.ShouldBe(3);
        overview.Growth[^1].Added.Select(c => c.Id).ShouldBe([busy.Client!.Id, gone], ignoreOrder: true);

        // Another domain's clients aren't counted.
        (await (await Client.GetAsync("/api/clients/overview", siteAdmin.AccessToken)).ReadAsync<ClientOverviewResponse>()).Clients.ShouldBe(0);
    }
}

/// <summary>Its own database, so its client doesn't change the overview's counts.</summary>
public class ClientCheckTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task The_client_form_hears_about_a_name_or_contact_already_used_before_saving()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var existing = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);
        var email = await _steps.ScalarAsync<string>("SELECT value FROM client_contacts WHERE client_id = @id", ("@id", existing.Id));

        async Task<ClientCheckResponse> CheckAsync(object body)
        {
            var response = await Client.PostJsonAsync("/api/clients/check", body, admin.AccessToken);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<ClientCheckResponse>();
        }

        var dupe = await CheckAsync(new { name = existing.Name.ToUpperInvariant(), contacts = new[] { new { kind = "email", value = " " + email.ToUpperInvariant() } } });
        dupe.NameTaken.ShouldBeTrue();
        dupe.ContactTakenBy!.ClientId.ShouldBe(existing.Id);

        (await CheckAsync(new { name = existing.Name, contacts = new[] { new { kind = "email", value = email } }, exceptClientId = existing.Id }))
            .ShouldBe(new ClientCheckResponse(false, null));
        (await CheckAsync(new { name = "Someone new", contacts = Array.Empty<object>() })).ShouldBe(new ClientCheckResponse(false, null));
    }
}
