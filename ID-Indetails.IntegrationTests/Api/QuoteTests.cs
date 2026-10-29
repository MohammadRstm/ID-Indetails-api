using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Quotes: the whole project's price for its client, TVA in it; made and sent by whoever sends invoices.</summary>
public class QuoteTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    [Fact]
    public async Task A_quote_snapshots_the_payment_with_its_tva_and_is_sent_and_downloaded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var project = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa interiors", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 10_000m, tvaRate = 0.15m,
            description = "Three bedrooms, two baths and the living room.",
        }, superAdmin.AccessToken));

        var quote = await OkAsync<QuoteResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/quotes", superAdmin.AccessToken));
        quote.ShouldSatisfyAllConditions(
            q => q.Number.ShouldStartWith($"QU-{DateTime.UtcNow.Year}-"),
            q => q.NetUsd.ShouldBe(10_000m),
            q => q.TvaRate.ShouldBe(0.15m),
            q => q.TotalUsd.ShouldBe(11_500m),
            q => q.Scope.ShouldBe("Three bedrooms, two baths and the living room."),
            q => q.Client.Id.ShouldBe(client.Id));

        var pdf = await Client.GetAsync($"/api/quotes/{quote.Id}/pdf", superAdmin.AccessToken);
        ((int)pdf.StatusCode).ShouldBe(200);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var sent = await OkAsync<QuoteResponse>(Client.PostJsonAsync($"/api/quotes/{quote.Id}/deliveries",
            new { channel = "whatsapp", recipient = "+961 3 123 456" }, superAdmin.AccessToken));
        sent.Deliveries.Single().Channel.ShouldBe(DeliveryChannel.Whatsapp);

        // a change of TVA makes the next quote different; the first stays as it was
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{project.Id}/payment",
            new { revenue = 10_000m, tvaRate = 0.11m, description = "Lower rate" }, superAdmin.AccessToken));
        var second = await OkAsync<QuoteResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/quotes", superAdmin.AccessToken));
        second.TotalUsd.ShouldBe(11_100m);
        second.Number.ShouldNotBe(quote.Number);
        var all = await OkAsync<List<QuoteResponse>>(Client.GetAsync($"/api/projects/{project.Id}/quotes", superAdmin.AccessToken));
        all.Select(q => q.Id).ShouldBe([second.Id, quote.Id]);
        all[1].TotalUsd.ShouldBe(11_500m);
    }

    [Fact]
    public async Task An_execution_site_has_no_quote_and_its_creation_asks_nobody_for_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await Projects.SetWorkPriceAsync(site.Id, 8000m);

        await (await Client.PostEmptyAsync($"/api/projects/{site.Id}/quotes", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.QuoteNotForExecution);

        // a site made by an admin who can't send quotes asks no one for one, unlike a Design project
        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);
        var byAdmin = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "No quote", domainCode = DomainCodes.Execution, clientId = client.Id, staffIds = new[] { await Projects.SiteStaffAsync() } }, admin.AccessToken));
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE kind = 'quote_due' AND project_id = @p", ("@p", byAdmin.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Only_client_work_with_a_payment_is_quoted_and_only_by_whoever_sends_invoices()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var unpriced = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Not priced yet", domainCode = DomainCodes.Design, clientId = client.Id }, superAdmin.AccessToken));
        await (await Client.PostEmptyAsync($"/api/projects/{unpriced.Id}/quotes", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.QuoteNotPossible);

        var internalWork = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Studio upkeep", domainCode = DomainCodes.Design, billingType = "internal" }, superAdmin.AccessToken));
        await (await Client.PostEmptyAsync($"/api/projects/{internalWork.Id}/quotes", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.QuoteNotPossible);

        await (await Client.PostEmptyAsync($"/api/projects/{unpriced.Id}/quotes", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_client_project_made_by_an_admin_asks_whoever_sends_quotes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);

        var byAdmin = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Needs a quote", domainCode = DomainCodes.Design, clientId = client.Id }, admin.AccessToken));
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE kind = 'quote_due' AND project_id = @p AND recipient_id = @to",
            ("@p", byAdmin.Id), ("@to", superAdmin.User.Id))).ShouldBe(1);

        // a super admin is asked on the spot instead
        var bySuperAdmin = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Quoted on the spot", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 500m }, superAdmin.AccessToken));
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE kind = 'quote_due' AND project_id = @p", ("@p", bySuperAdmin.Id))).ShouldBe(0);
    }
}
