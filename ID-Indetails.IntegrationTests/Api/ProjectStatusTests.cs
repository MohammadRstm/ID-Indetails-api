using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectStatusTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<ProjectResponse> ChangeAsync(long projectId, string action, string token)
    {
        var response = await Client.PostEmptyAsync($"/api/projects/{projectId}/{action}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectResponse> CompleteWithAsync(long projectId, object body, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/complete", body, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private async Task PayAsync(long projectId, decimal amount, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", new { amount, currency = "USD" }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A site at 10% supervision and 11% TVA; with <paramref name="work"/> above 0, a client BOQ of that is agreed.</summary>
    private async Task<ProjectResponse> SiteAsync(string token, decimal work)
    {
        var site = await Projects.CreateProjectAsync(token, DomainCodes.Execution, projectFee: 0m);
        var priced = await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { supervisionRate = 0.10m, tvaRate = 0.11m, description = "The usual rates" }, token);
        priced.IsSuccessStatusCode.ShouldBeTrue(await priced.Content.ReadAsStringAsync());
        if (work > 0)
        {
            await Projects.AgreeWorkPriceAsync(token, site.Id, work);
        }

        return site;
    }

    private async Task<BoqListResponse> BoqsAsync(long projectId, string token)
        => await (await Client.GetAsync($"/api/projects/{projectId}/boqs", token)).ReadAsync<BoqListResponse>();

    /// <summary>Requirements come through their own endpoints in the requirements phase; here they are seeded directly.</summary>
    private async Task AddRequirementsAsync(long projectId, long userId, int open, int done)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        for (var i = 0; i < open + done; i++)
        {
            await using var insert = new MySqlCommand(
                "INSERT INTO project_requirements (project_id, text, is_done, done_at, auto_done, created_at, created_by_id) " +
                "VALUES (@p, 'Deliverable', @d, IF(@d, UTC_TIMESTAMP(), NULL), 0, UTC_TIMESTAMP(), @u)", connection);
            insert.Parameters.AddWithValue("@p", projectId);
            insert.Parameters.AddWithValue("@d", i >= open);
            insert.Parameters.AddWithValue("@u", userId);
            await insert.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Completing_finalises_and_reopening_undoes_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await AddRequirementsAsync(project.Id, admin.User.Id, open: 3, done: 1);

        (await ChangeAsync(project.Id, "hold", admin.AccessToken)).Status.ShouldBe(ProjectStatus.Hold);
        var held = await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>();
        held.HeldSince.ShouldNotBeNull();
        (DateTime.UtcNow - held.HeldSince.Value).ShouldBeLessThan(TimeSpan.FromMinutes(1));
        var completed = await ChangeAsync(project.Id, "complete", admin.AccessToken);
        completed.Status.ShouldBe(ProjectStatus.Completed);
        completed.ProgressPct.ShouldBe(100);
        completed.CompletedAt.ShouldNotBeNull();
        completed.Money!.IsLocked.ShouldBeTrue();
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM project_requirements WHERE project_id = @p AND auto_done = 1", ("@p", project.Id)))
            .ShouldBe(3);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 1, description = "After the end" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.StatusUnchanged);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvalidStatusChange);

        var reopened = await ChangeAsync(project.Id, "reopen", admin.AccessToken);
        reopened.Status.ShouldBe(ProjectStatus.Active);
        reopened.ProgressPct.ShouldBe(25);
        reopened.CompletedAt.ShouldBeNull();
        reopened.Money!.IsLocked.ShouldBeFalse();

        var kinds = await _steps.ScalarAsync<string>(
            "SELECT GROUP_CONCAT(kind ORDER BY id) FROM activity_events WHERE project_id = @p AND kind <> 'project_created' AND kind <> 'revenue_set'",
            ("@p", project.Id));
        // The seeded requirements bypassed the API, so the first change also brings the cached progress up to 25%.
        kinds.ShouldBe("project_hold,progress_set,project_completed,project_finalized,progress_set,project_reopened,progress_set");
    }

    [Fact]
    public async Task Archiving_unfinished_work_finalises_it_and_projects_without_requirements_read_as_done()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var archived = await ChangeAsync(project.Id, "archive", superAdmin.AccessToken);
        archived.Status.ShouldBe(ProjectStatus.Archived);
        archived.FinalizedAt.ShouldNotBeNull();
        archived.ProgressPct.ShouldBe(100);

        (await ChangeAsync(project.Id, "reopen", superAdmin.AccessToken)).ProgressPct.ShouldBe(0);

        var completed = await ChangeAsync(project.Id, "complete", superAdmin.AccessToken);
        var archivedAfter = await ChangeAsync(project.Id, "archive", superAdmin.AccessToken);
        archivedAfter.FinalizedAt.ShouldBe(completed.FinalizedAt);
    }

    [Fact]
    public async Task Finalising_an_execution_site_freezes_its_agreed_amount()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        var completed = await ChangeAsync(site.Id, "complete", admin.AccessToken);
        completed.Money!.Revenue.ShouldBe(1000m);
        (await _steps.ScalarAsync<decimal>("SELECT revenue FROM projects WHERE id = @p", ("@p", site.Id))).ShouldBe(1000m);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { supervisionRate = 0.2m, description = "Late" }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);

        await ChangeAsync(site.Id, "reopen", admin.AccessToken);
        (await _steps.ScalarAsync<decimal>("SELECT revenue FROM projects WHERE id = @p", ("@p", site.Id))).ShouldBe(0m);
        var repriced = await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { supervisionRate = 0.1m, description = "Reopened" }, admin.AccessToken))
            .ReadAsync<ProjectResponse>();
        repriced.Money!.ShouldSatisfyAllConditions(
            m => m.SupervisionUsd.ShouldBe(100m),
            m => m.Revenue.ShouldBe(1100m));
    }

    [Fact]
    public async Task Completing_a_site_that_was_paid_ahead_can_keep_it_as_a_closing_vo_so_the_price_equals_what_was_received()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        // 1,000 of work: 100 supervision, 110 TVA, 1,210 for the client; the client paid 1,500
        var site = await SiteAsync(token, 1_000m);
        await PayAsync(site.Id, 1_500m, token);

        var completed = await CompleteWithAsync(site.Id, new { keepPaidAhead = true }, token);

        // 1,239.67 of work + 123.97 supervision + 136.36 TVA = 1,500.00
        completed.Money!.ShouldSatisfyAllConditions(
            m => m.IsLocked.ShouldBeTrue(),
            m => m.ProjectFee.ShouldBe(1_239.67m),
            m => m.SupervisionUsd.ShouldBe(123.97m),
            m => m.Revenue.ShouldBe(1_363.64m),
            m => m.RevenueWithTva.ShouldBe(1_500m),
            m => m.Billing.Received.ShouldBe(1_500m),
            m => m.Billing.PaidAhead.ShouldBe(0m));
        (await _steps.ScalarAsync<decimal>("SELECT revenue FROM projects WHERE id = @p", ("@p", site.Id))).ShouldBe(1_363.64m);

        // the agreed client BOQ stays; a VO for the rest is agreed beside it, as any VO is
        var boqs = await BoqsAsync(site.Id, token);
        boqs.Client.ShouldHaveSingleItem().TotalUsd.ShouldBe(1_000m);
        var vo = boqs.Vos.ShouldHaveSingleItem();
        vo.ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BoqKind.Vo),
            b => b.Status.ShouldBe(BoqStatus.Agreed),
            b => b.Title.ShouldBe("Paid beyond the agreed price"),
            b => b.Reason.ShouldBe("Kept when the project was completed: the client paid more than the agreed price."),
            b => b.TotalUsd.ShouldBe(239.67m),
            b => b.AgreedBy!.Id.ShouldBe(superAdmin.User.Id),
            b => b.Number.ShouldNotBeNullOrEmpty());
        boqs.Price!.WorkUsd.ShouldBe(1_239.67m);

        // written like any price change: the payment history, the feed and the BOQ's own event
        var history = await (await Client.GetAsync($"/api/projects/{site.Id}/payment-changes", token)).ReadAsync<List<ProjectPaymentChangeResponse>>();
        history[0].ShouldSatisfyAllConditions(
            h => h.Amount.ShouldBe(1_239.67m),
            h => h.PreviousAmount.ShouldBe(1_000m),
            h => h.Description.ShouldBe(vo.Number),
            h => h.ChangedBy!.Id.ShouldBe(superAdmin.User.Id));
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'project_fee_set' AND amount = 1239.67 AND prev_amount = 1000", ("@p", site.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'boq' AND subject_id = @b AND kind = 'boq_agreed'", ("@b", vo.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'project_finalized'", ("@p", site.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_site_with_no_agreed_price_that_was_paid_keeps_it_as_a_client_boq()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var site = await SiteAsync(token, work: 0);
        await PayAsync(site.Id, 1_210m, token);

        var completed = await CompleteWithAsync(site.Id, new { keepPaidAhead = true }, token);

        completed.Money!.ShouldSatisfyAllConditions(
            m => m.ProjectFee.ShouldBe(1_000m),
            m => m.Revenue.ShouldBe(1_100m),
            m => m.RevenueWithTva.ShouldBe(1_210m));
        var boqs = await BoqsAsync(site.Id, token);
        boqs.Vos.ShouldBeEmpty();
        var closing = boqs.Client.ShouldHaveSingleItem();
        closing.ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BoqKind.Client),
            b => b.Status.ShouldBe(BoqStatus.Agreed),
            b => b.Title.ShouldBe("Paid beyond the agreed price"),
            b => b.TotalUsd.ShouldBe(1_000m));
    }

    [Fact]
    public async Task Without_the_switch_or_a_body_a_site_paid_ahead_completes_as_before_and_the_balance_stays_owed_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;

        foreach (var body in new object?[] { null, new { keepPaidAhead = false }, new { } })
        {
            var site = await SiteAsync(token, 1_000m);
            await PayAsync(site.Id, 1_500m, token);

            var completed = body is null ? await ChangeAsync(site.Id, "complete", token) : await CompleteWithAsync(site.Id, body, token);

            completed.Status.ShouldBe(ProjectStatus.Completed);
            completed.Money!.ShouldSatisfyAllConditions(
                m => m.ProjectFee.ShouldBe(1_000m),
                m => m.Revenue.ShouldBe(1_100m),
                m => m.RevenueWithTva.ShouldBe(1_210m),
                m => m.Billing.PaidAhead.ShouldBe(290m));
            var boqs = await BoqsAsync(site.Id, token);
            boqs.Vos.ShouldBeEmpty();
            boqs.Client.ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task The_switch_changes_nothing_when_the_client_did_not_pay_beyond_the_price()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var under = await SiteAsync(token, 1_000m);
        await PayAsync(under.Id, 500m, token);
        var exact = await SiteAsync(token, 1_000m);
        await PayAsync(exact.Id, 1_210m, token);
        var none = await SiteAsync(token, 1_000m);

        foreach (var site in new[] { under, exact, none })
        {
            var completed = await CompleteWithAsync(site.Id, new { keepPaidAhead = true }, token);

            completed.Money!.ProjectFee.ShouldBe(1_000m);
            completed.Money.Revenue.ShouldBe(1_100m);
            (await BoqsAsync(site.Id, token)).Vos.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Completing_a_design_project_paid_ahead_can_raise_its_payment_to_what_was_received()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var client = await Projects.CreateClientAsync(token, DomainCodes.Design);
        // 1,000 at 11% TVA: the client owes 1,110 and paid 1,500
        var kept = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Villa interiors", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 1_000m }, token)).ReadAsync<ProjectResponse>();
        var declined = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Office interiors", domainCode = DomainCodes.Design, clientId = client.Id, revenue = 1_000m }, token)).ReadAsync<ProjectResponse>();
        await PayAsync(kept.Id, 1_500m, token);
        await PayAsync(declined.Id, 1_500m, token);

        var completed = await CompleteWithAsync(kept.Id, new { keepPaidAhead = true }, token);

        // 1,351.35 net + 11% TVA = 1,500.00
        completed.Money!.ShouldSatisfyAllConditions(
            m => m.IsLocked.ShouldBeTrue(),
            m => m.Revenue.ShouldBe(1_351.35m),
            m => m.RevenueWithTva.ShouldBe(1_500m),
            m => m.Billing.PaidAhead.ShouldBe(0m));
        var history = await (await Client.GetAsync($"/api/projects/{kept.Id}/payment-changes", token)).ReadAsync<List<ProjectPaymentChangeResponse>>();
        history[0].ShouldSatisfyAllConditions(
            h => h.Amount.ShouldBe(1_351.35m),
            h => h.PreviousAmount.ShouldBe(1_000m),
            h => h.TvaRate.ShouldBe(0.11m),
            h => h.Description.ShouldBe("Kept when the project was completed: the client paid more than the agreed price."),
            h => h.ChangedBy!.Id.ShouldBe(superAdmin.User.Id));
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'revenue_changed' AND amount = 1351.35 AND prev_amount = 1000", ("@p", kept.Id))).ShouldBe(1);

        var left = await CompleteWithAsync(declined.Id, new { keepPaidAhead = false }, token);
        left.Money!.ShouldSatisfyAllConditions(m => m.Revenue.ShouldBe(1_000m), m => m.Billing.PaidAhead.ShouldBe(390m));
    }

    [Fact]
    public async Task Only_status_managers_of_the_domain_change_status()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync("/api/projects/999999/complete", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
