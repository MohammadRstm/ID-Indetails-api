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
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

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
    public async Task Execution_projects_are_client_work_with_a_fee_and_markup_and_a_calculated_revenue()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);

        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        site.Money!.ProjectFee.ShouldBe(1000m);
        site.Money.MaterialMarkup.ShouldBe(0.10m);
        site.Money.Revenue.ShouldBe(1000m);

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

        var priced = await (await Client.GetAsync($"/api/projects/{site.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>();
        priced.Money!.MaterialCharge.ShouldBe(270m);
        priced.Money.Revenue.ShouldBe(1270m);

        var repriced = await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { projectFee = 1500m, materialMarkup = 0.25m }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        repriced.Money!.Revenue.ShouldBe(1800m);

        var client = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Execution);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Internal site", billingType = "internal", projectFee = 1, materialMarkup = 0 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "No fee", clientId = client.Id }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects", new { title = "Markup", clientId = client.Id, projectFee = 1, materialMarkup = 6 }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/billing", new { billingType = "client", clientId = client.Id, revenue = 5 }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task Details_owner_and_design_billing_can_be_changed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, outsider) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var updated = await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new
        {
            title = "Renamed",
            tint = "#1F6FEB",
            targetDate = "2026-12-31",
            ownerId = employee.User.Id,
        }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        updated.Title.ShouldBe("Renamed");
        updated.Owner.Id.ShouldBe(employee.User.Id);
        updated.TargetDate.ShouldBe(new DateOnly(2026, 12, 31));

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = outsider.User.Id }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.OwnerNotAllowed);
        (await Client.PutJsonAsync($"/api/projects/{project.Id}", new { title = "X", ownerId = superAdmin.User.Id }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var other = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);
        var moved = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing",
            new { billingType = "client", clientId = other.Id, revenue = 7000m }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        moved.Client!.Id.ShouldBe(other.Id);
        moved.Money!.Revenue.ShouldBe(7000m);

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
            title = "Apartment fit-out", domainCode = DomainCodes.Execution, clientId = client.Id, projectFee = 1000m, materialMarkup = 0.10m,
            location = "  Achrafieh, Beirut  ",
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
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
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
        await (await Client.PutJsonAsync($"/api/projects/{design.Id}/pricing", new { projectFee = 1, materialMarkup = 0 }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
