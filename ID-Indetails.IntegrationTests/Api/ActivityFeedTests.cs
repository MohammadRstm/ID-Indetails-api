using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ActivityFeedTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task The_feed_shows_work_with_its_entry_and_hides_amounts_from_non_money_viewers()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 7000);
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 45, "Poster layout");

        var work = await (await Client.GetAsync($"/api/activity?group=work&projectId={project.Id}", admin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        var logged = work.Items.Single(i => i.Kind == ActivityEventKinds.WorkEntryLogged);
        logged.Group.ShouldBe(ActivityGroup.Work);
        logged.Actor!.Id.ShouldBe(employee.User.Id);
        logged.WorkEntry!.Id.ShouldBe(entry.Id);
        logged.WorkEntry.Description.ShouldBe("Poster layout");
        logged.Payload!.Value.GetProperty("minutes").GetInt32().ShouldBe(45);

        var money = await (await Client.GetAsync($"/api/activity?group=money&projectId={project.Id}", admin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        money.Items.Single(i => i.Kind == ActivityEventKinds.RevenueSet).Amount.ShouldBe(7000m);

        // A person who may read the feed but not money or everyone's work.
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ActivityView, action = "grant", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var limited = await (await Client.GetAsync($"/api/activity?projectId={project.Id}", lead.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        limited.Items.ShouldNotContain(i => i.Group == ActivityGroup.Work);
        limited.Items.Single(i => i.Kind == ActivityEventKinds.RevenueSet).Amount.ShouldBeNull();
        await (await Client.GetAsync("/api/activity/today", lead.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var today = await (await Client.GetAsync("/api/activity/today", admin.AccessToken)).ReadAsync<ActivityTodayResponse>();
        today.Date.ShouldBe(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"))));
        today.ByLoggedHour.Sum(h => h.Minutes).ShouldBe(today.Minutes);
    }

    [Fact]
    public async Task A_markup_change_keeps_the_exact_markup_in_its_payload_for_money_viewers_only()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { projectFee = 1000m, materialMarkup = 0.125m }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var money = await (await Client.GetAsync($"/api/activity?group=money&projectId={site.Id}", admin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        var markupSet = money.Items.Single(i => i.Kind == ActivityEventKinds.MaterialMarkupSet);
        // 12.5% exactly: the amount columns keep 2 decimals and would have stored 0.13.
        markupSet.Payload!.Value.GetProperty("markup").GetDecimal().ShouldBe(0.125m);
        markupSet.Payload.Value.GetProperty("prevMarkup").GetDecimal().ShouldBe(0.10m);
        markupSet.Amount.ShouldBeNull();
        markupSet.PrevAmount.ShouldBeNull();

        // Someone who reads the feed without financial:view sees the change, not the markup.
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ActivityView, action = "grant", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var limited = await (await Client.GetAsync($"/api/activity?projectId={site.Id}", lead.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        limited.Items.Single(i => i.Kind == ActivityEventKinds.MaterialMarkupSet).Payload.ShouldBeNull();
    }

    [Fact]
    public async Task Other_domains_and_company_events_stay_out_of_a_domain_feed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89500m }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var forExecution = await (await Client.GetAsync("/api/activity?pageSize=100", executionAdmin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        forExecution.Items.ShouldNotContain(i => i.Project != null && i.Project.Id == design.Id);
        forExecution.Items.ShouldContain(i => i.Project != null && i.Project.Id == site.Id);
        forExecution.Items.ShouldNotContain(i => i.Domain == null);

        (await (await Client.GetAsync("/api/activity?group=company", designAdmin.AccessToken)).ReadAsync<PagedResponse<ActivityItemResponse>>())
            .TotalCount.ShouldBe(0);
        (await (await Client.GetAsync("/api/activity?group=billing", superAdmin.AccessToken)).ReadAsync<PagedResponse<ActivityItemResponse>>())
            .Items.ShouldContain(i => i.Kind == ActivityEventKinds.ExchangeRateSet);

        await (await Client.GetAsync("/api/activity", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/activity?group=other", designAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_client_event_names_its_client_and_shows_under_that_client()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rami Khoury",
            label = "Client",
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("client"), isWhatsapp = false } },
        }, admin.AccessToken)).ReadAsync<ClientResponse>();

        var feed = await (await Client.GetAsync($"/api/activity?clientId={client.Id}", admin.AccessToken))
            .ReadAsync<PagedResponse<ActivityItemResponse>>();
        var added = feed.Items.Single(i => i.Kind == ActivityEventKinds.ClientAdded);
        added.Client!.Id.ShouldBe(client.Id);
        added.Client.Name.ShouldBe("Rami Khoury");
    }
}
