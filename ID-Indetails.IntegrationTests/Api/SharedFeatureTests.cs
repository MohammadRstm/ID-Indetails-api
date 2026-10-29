using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Shared project features that were only tested on Design projects, now on Execution sites too, plus
/// resuming from hold and the project-list and activity filters.
/// </summary>
public class SharedFeatureTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Execution_sites_use_requirements_expenses_and_project_edits_like_design()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        var requirement = await Client.PostJsonAsync($"/api/projects/{site.Id}/requirements",
            new { text = "Pour the slab", assigneeIds = new[] { siteWorker.User.Id } }, admin.AccessToken);
        ((int)requirement.StatusCode).ShouldBe(201, await requirement.Content.ReadAsStringAsync());
        var created = await requirement.ReadAsync<RequirementResponse>();
        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"/api/projects/{site.Id}/requirements/{created.Id}/done", admin.AccessToken));
        var requirements = await OkAsync<RequirementListResponse>(Client.GetAsync($"/api/projects/{site.Id}/requirements", siteWorker.AccessToken));
        requirements.ProgressPct.ShouldBe(100);

        // a site's costs are its site expenses
        var expense = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Crane hire", amount = 350m, category = "contractor" } } }, admin.AccessToken);
        ((int)expense.StatusCode).ShouldBe(201, await expense.Content.ReadAsStringAsync());
        var expenses = await OkAsync<SiteExpenseListResponse>(Client.GetAsync($"/api/projects/{site.Id}/site-expenses", admin.AccessToken));
        expenses.All.ByCurrency.ShouldHaveSingleItem().Total.ShouldBe(350m);

        var renamed = await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{site.Id}",
            new { title = "Tower B", ownerId = admin.User.Id }, admin.AccessToken));
        renamed.Title.ShouldBe("Tower B");

        // A Design admin can't reach the site at all.
        await (await Client.GetAsync($"/api/projects/{site.Id}/requirements", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}", new { title = "Taken", ownerId = designAdmin.User.Id }, designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Execution_invoices_take_payments_on_account_and_void_like_design()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        var draft = await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/projects/{site.Id}/invoices",
            new { lines = new[] { new { description = "Stage 1", amount = 500m } } }, admin.AccessToken));
        var invoice = await OkAsync<InvoiceResponse>(Client.PostEmptyAsync($"/api/invoices/{draft.Id}/issue", admin.AccessToken));
        invoice.Number!.ShouldStartWith("EX-");

        var onAccount = await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 200m, currency = "USD" }, admin.AccessToken);
        var payment = await onAccount.ReadAsync<PaymentResponse>();
        (await OkAsync<PaymentResponse>(Client.PutJsonAsync($"/api/payments/{payment.Id}/invoice", new { invoiceId = invoice.Id }, admin.AccessToken)))
            .InvoiceId.ShouldBe(invoice.Id);

        await (await Client.PostJsonAsync($"/api/invoices/{invoice.Id}/void", new { reason = "Wrong stage" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvoiceHasPayments);
        await OkAsync<PaymentResponse>(Client.PostJsonAsync($"/api/payments/{payment.Id}/void", new { reason = "Bounced" }, admin.AccessToken));
        (await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/invoices/{invoice.Id}/void", new { reason = "Wrong stage" }, admin.AccessToken)))
            .VoidedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_project_on_hold_resumes_to_active()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        (await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", admin.AccessToken))).Status.ShouldBe(ProjectStatus.Hold);
        var resumed = await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/resume", admin.AccessToken));
        resumed.Status.ShouldBe(ProjectStatus.Active);
        resumed.FinalizedAt.ShouldBeNull();

        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/resume", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.StatusUnchanged);
    }

    [Fact]
    public async Task The_project_list_filters_by_search_status_client_and_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var first = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var second = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(token, DomainCodes.Execution);
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{second.Id}/hold", token));

        async Task<List<long>> IdsAsync(string query)
            => (await OkAsync<PagedResponse<ProjectSummaryResponse>>(Client.GetAsync($"/api/projects?pageSize=100&{query}", token)))
                .Items.Select(p => p.Id).ToList();

        (await IdsAsync($"search={Uri.EscapeDataString(first.Title)}")).ShouldBe([first.Id]);
        var onHold = await IdsAsync("status=hold");
        onHold.ShouldContain(second.Id);
        onHold.ShouldNotContain(first.Id);
        (await IdsAsync($"clientId={first.Client!.Id}")).ShouldBe([first.Id]);
        var execution = await IdsAsync("domain=execution");
        execution.ShouldContain(site.Id);
        execution.ShouldNotContain(first.Id);
    }

    [Fact]
    public async Task The_activity_feed_filters_by_client_and_person()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var mine = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var theirs = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var byClient = await OkAsync<PagedResponse<ActivityItemResponse>>(
            Client.GetAsync($"/api/activity?clientId={mine.Client!.Id}&pageSize=100", superAdmin.AccessToken));
        byClient.Items.ShouldNotBeEmpty();
        byClient.Items.ShouldAllBe(e => e.Client != null && e.Client.Id == mine.Client!.Id);

        var byPerson = await OkAsync<PagedResponse<ActivityItemResponse>>(
            Client.GetAsync($"/api/activity?userId={admin.User.Id}&pageSize=100", superAdmin.AccessToken));
        byPerson.Items.ShouldNotBeEmpty();
        byPerson.Items.ShouldAllBe(e => e.Actor != null && e.Actor.Id == admin.User.Id);
        byPerson.Items.ShouldNotContain(e => e.Project != null && e.Project.Id == theirs.Id);
    }

    [Fact]
    public async Task The_activity_feed_shows_one_domain_when_asked()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        Task<PagedResponse<ActivityItemResponse>> FeedAsync(string domain, string token)
            => OkAsync<PagedResponse<ActivityItemResponse>>(Client.GetAsync($"/api/activity?domain={domain}&pageSize=100", token));

        // Company-level events are left out too: they belong to the company view.
        var design = await FeedAsync(DomainCodes.Design, superAdmin.AccessToken);
        design.Items.ShouldNotBeEmpty();
        design.Items.ShouldAllBe(e => e.Domain == DomainCodes.Design);
        var execution = await FeedAsync(DomainCodes.Execution, superAdmin.AccessToken);
        execution.Items.ShouldNotBeEmpty();
        execution.Items.ShouldAllBe(e => e.Domain == DomainCodes.Execution);

        // A domain the caller can't see gives nothing, and only real domains are accepted.
        (await FeedAsync(DomainCodes.Execution, designAdmin.AccessToken)).Items.ShouldBeEmpty();
        await (await Client.GetAsync("/api/activity?domain=company", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
