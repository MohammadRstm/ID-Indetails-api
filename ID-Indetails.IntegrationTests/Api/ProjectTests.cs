using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Admin_creates_design_client_and_internal_projects()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 12_500.50m);
        project.Domain.ShouldBe(DomainCodes.Design);
        project.Status.ShouldBe(ProjectStatus.Active);
        project.Owner.Id.ShouldBe(admin.User.Id);
        project.Client.ShouldNotBeNull();
        project.Money!.Revenue.ShouldBe(12_500.50m);

        var internalProject = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Website refresh", billingType = "internal" }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        internalProject.BillingType.ShouldBe(BillingType.Internal);
        internalProject.Client.ShouldBeNull();

        await (await Client.PostJsonAsync("/api/projects", new { title = "No client" }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Internal", billingType = "internal", revenue = 10 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Fee", clientId = project.Client!.Id, projectFee = 5, materialMarkup = 0.1 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @id AND kind IN ('project_created','revenue_set')", ("@id", project.Id)))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Execution_projects_are_client_work_with_a_work_price_supervision_and_markup_and_a_calculated_revenue()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);

        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        site.Money!.ProjectFee.ShouldBe(1000m);
        site.Money.MaterialMarkup.ShouldBe(0.10m);
        site.Money.Revenue.ShouldBe(1000m);
        site.Money.SupervisionRate.ShouldBe(0m);
        site.Money.SupervisionUsd.ShouldBe(0m);

        // A material costing 200 at 10% markup, and one priced by hand at 50.
        await using (var connection = await api.Database.OpenConnectionAsync())
        {
            await using var insert = new MySqlConnector.MySqlCommand(
                "INSERT INTO project_materials (project_id, name, amount, currency, client_price, added_at, added_by_id) VALUES " +
                "(@p, 'Cement', 200, 'USD', NULL, UTC_TIMESTAMP(), @u), (@p, 'Paint', 30, 'USD', 50, UTC_TIMESTAMP(), @u)", connection);
            insert.Parameters.AddWithValue("@p", site.Id);
            insert.Parameters.AddWithValue("@u", admin.User.Id);
            await insert.ExecuteNonQueryAsync();
        }

        // not sent to the client yet: their cost counts, their client prices don't
        var priced = await (await Client.GetAsync($"/api/projects/{site.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>();
        priced.Money!.MaterialCharge.ShouldBe(0m);
        priced.Money.UnsentMaterialCharge.ShouldBe(270m);
        priced.Money.UnsentMaterials.ShouldBe(2);
        priced.Money.Revenue.ShouldBe(1000m);

        // a bigger BOQ was agreed (the price is never typed in), and supervision is 10% of it
        await Projects.SetWorkPriceAsync(site.Id, 1500m);
        var repriced = await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { materialMarkup = 0.25m, supervisionRate = 0.10m, description = "Bigger site" }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        repriced.Money!.ShouldSatisfyAllConditions(
            m => m.SupervisionRate.ShouldBe(0.10m),
            m => m.SupervisionUsd.ShouldBe(150m),
            m => m.Revenue.ShouldBe(1650m));
        repriced.Money!.UnsentMaterialCharge.ShouldBe(300m);

        // sent on a statement, they join the revenue at the prices the client was shown
        await Projects.SendStatementAsync(admin.AccessToken, site.Id);
        var sent = await (await Client.GetAsync($"/api/projects/{site.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>();
        sent.Money!.MaterialCharge.ShouldBe(300m);
        sent.Money.MaterialProfit.ShouldBe(70m);
        sent.Money.UnsentMaterials.ShouldBe(0);
        sent.Money.Revenue.ShouldBe(1950m);

        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Internal site", billingType = "internal", projectFee = 1, materialMarkup = 0 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Markup", clientId = client.Id, projectFee = 1, materialMarkup = 6 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        // the work price comes from an agreed BOQ: it can't be typed in, at the start or after
        (await (await Client.PostJsonAsync("/api/projects", new { title = "Priced", clientId = client.Id, projectFee = 1000m, materialMarkup = 0.1m }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("projectFee");
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { projectFee = 2000m, materialMarkup = 0.1m, description = "Typed" }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Too much", clientId = client.Id, supervisionRate = 1.5m }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/billing", new { billingType = "client", clientId = client.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task Details_owner_and_design_billing_can_be_changed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, secondAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, outsider) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var updated = await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new
        {
            title = "Renamed",
            tint = "#1F6FEB",
            targetDate = "2026-12-31",
            ownerId = secondAdmin.User.Id,
        }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        updated.Title.ShouldBe("Renamed");
        updated.Owner.Id.ShouldBe(secondAdmin.User.Id);
        updated.TargetDate.ShouldBe(new DateOnly(2026, 12, 31));

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = outsider.User.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.OwnerNotAllowed);
        // employees work on projects but don't own them
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = employee.User.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.OwnerNotAllowed);
        // a super admin may own it, but only someone who sees both domains (and so super admins) can pick one
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = superAdmin.User.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.OwnerNotAllowed);
        (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = superAdmin.User.Id }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var other = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);
        var moved = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing",
            new { billingType = "client", clientId = other.Id }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        moved.Client!.Id.ShouldBe(other.Id);
        var paid = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment",
            new { revenue = 7000m, description = "The new client's offer" }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        paid.Money!.Revenue.ShouldBe(7000m);

        var madeInternal = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing",
            new { billingType = "internal" }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        madeInternal.Client.ShouldBeNull();
        madeInternal.Money!.Revenue.ShouldBe(0m);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @id AND kind = 'revenue_changed' AND amount = 0 AND prev_amount = 7000",
            ("@id", project.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_project_can_say_where_the_work_is()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);

        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Apartment fit-out", domainCode = DomainCodes.Execution, clientId = client.Id,
            location = "  Achrafieh, Beirut  ", staffIds = new[] { await Projects.SiteStaffAsync() },
        }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();
        project.Location.ShouldBe("Achrafieh, Beirut");

        var list = await (await Client.GetAsync("/api/projects?domain=execution&pageSize=100", admin.AccessToken)).ReadAsync<PagedResponse<ProjectSummaryResponse>>();
        list.Items.Single(p => p.Id == project.Id).Location.ShouldBe("Achrafieh, Beirut");

        async Task<ProjectResponse> EditAsync(object body)
        {
            var response = await Client.PutJsonAsync($"/api/projects/{project.Id}", body, admin.AccessToken);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<ProjectResponse>();
        }

        (await EditAsync(new { title = "Apartment fit-out", ownerId = admin.User.Id, location = "Jounieh" })).Location.ShouldBe("Jounieh");
        // optional: an edit without one clears it
        (await EditAsync(new { title = "Apartment fit-out", ownerId = admin.User.Id })).Location.ShouldBeNull();

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "Apartment fit-out", location = new string('x', 201) }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Archived_and_other_domain_clients_cannot_be_chosen()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var archived = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);
        await Client.PostEmptyAsync($"/api/clients/{archived.Id}/archive", admin.AccessToken);
        var foreign = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.PostJsonAsync("/api/projects", new { title = "A", clientId = archived.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ClientIsArchived);
        await (await Client.PostJsonAsync("/api/projects", new { title = "B", clientId = foreign.Id }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Employees_see_their_domain_projects_without_money_and_cannot_change_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var seen = await (await Client.GetAsync($"/api/projects/{design.Id}", employee.AccessToken)).ReadAsync<ProjectResponse>();
        seen.Money.ShouldBeNull();
        seen.Client.ShouldNotBeNull();

        var list = await (await Client.GetAsync("/api/projects?pageSize=100", employee.AccessToken))
            .ReadAsync<PagedResponse<ProjectSummaryResponse>>();
        list.Items.ShouldContain(p => p.Id == design.Id);
        list.Items.ShouldAllBe(p => p.Domain == DomainCodes.Design);

        await (await Client.GetAsync($"/api/projects/{site.Id}", employee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Mine", billingType = "internal" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/projects/{design.Id}", new { title = "X", ownerId = employee.User.Id }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // Another domain's admin can't reach the project either.
        await (await Client.PutJsonAsync($"/api/projects/{design.Id}", new { title = "X", ownerId = executionAdmin.User.Id }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync($"/api/projects/{design.Id}/pricing", new { materialMarkup = 0, description = "X" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_design_admin_runs_client_projects_without_seeing_or_setting_their_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);

        var created = await Client.PostJsonAsync("/api/projects", new { title = "Villa", clientId = client.Id }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();
        (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money.ShouldBeNull();

        await (await Client.PostJsonAsync("/api/projects", new { title = "Priced", clientId = client.Id, revenue = 5000m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing", new { billingType = "client", clientId = client.Id }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 5000m, description = "Agreed" }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{project.Id}/payment-changes", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/finance/design/summary", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task An_execution_admin_creates_a_site_without_its_pricing_and_cannot_set_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);

        var created = await Client.PostJsonAsync("/api/projects", new { title = "Lobby", clientId = client.Id, staffIds = new[] { await Projects.SiteStaffAsync() } }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var site = await (await Client.GetAsync($"/api/projects/{(await created.ReadAsync<ProjectResponse>()).Id}", superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        site.Money!.ProjectFee.ShouldBe(0m);
        site.Money.MaterialMarkup.ShouldBe(0m);
        // a site without pricing still starts at the default supervision
        site.Money.SupervisionRate.ShouldBe(0.10m);

        await (await Client.PostJsonAsync("/api/projects", new { title = "Priced", clientId = client.Id, materialMarkup = 0.10m, staffIds = new[] { await Projects.SiteStaffAsync() } }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
