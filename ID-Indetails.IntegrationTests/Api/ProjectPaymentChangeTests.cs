using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectPaymentChangeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<List<ProjectPaymentChangeResponse>> HistoryAsync(long projectId, string token)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/payment-changes", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<ProjectPaymentChangeResponse>>();
    }

    private async Task<ProjectResponse> PayAsync(long projectId, decimal revenue, string description, string token)
    {
        var response = await Client.PutJsonAsync($"/api/projects/{projectId}/payment", new { revenue, description }, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    [Fact]
    public async Task A_design_payment_starts_as_agreed_and_each_change_keeps_why_newest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5000m);

        var opening = (await HistoryAsync(project.Id, superAdmin.AccessToken)).ShouldHaveSingleItem();
        opening.Amount.ShouldBe(5000m);
        opening.PreviousAmount.ShouldBeNull();
        opening.MaterialMarkup.ShouldBeNull();
        opening.Description.ShouldBe(ProjectRules.AgreedAtStart);
        opening.ChangedBy!.Id.ShouldBe(superAdmin.User.Id);

        var changed = await PayAsync(project.Id, 6500m, "  The client added a guest house  ", superAdmin.AccessToken);
        changed.Money!.Revenue.ShouldBe(6500m);
        await PayAsync(project.Id, 6000m, "A discount for paying ahead", superAdmin.AccessToken);

        var history = await HistoryAsync(project.Id, superAdmin.AccessToken);
        history.Select(c => (c.Amount, c.PreviousAmount, c.Description)).ShouldBe(
        [
            (6000m, (decimal?)6500m, "A discount for paying ahead"),
            (6500m, 5000m, "The client added a guest house"),
            (5000m, null, ProjectRules.AgreedAtStart),
        ]);

        // the activity feed and expected revenue still see every change, without its description
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @id AND kind = 'revenue_changed'", ("@id", project.Id))).ShouldBe(2);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @id AND payload LIKE '%guest house%'", ("@id", project.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task A_design_payment_change_needs_a_description_and_a_new_amount()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5000m);
        var url = $"/api/projects/{project.Id}/payment";

        (await (await Client.PutJsonAsync(url, new { revenue = 6000m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("description");
        (await (await Client.PutJsonAsync(url, new { revenue = 6000m, description = "   " }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("description");
        (await (await Client.PutJsonAsync(url, new { revenue = 6000m, description = new string('x', ProjectRules.MaxPaymentDescriptionLength + 1) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("description");
        (await (await Client.PutJsonAsync(url, new { revenue = -1m, description = "Less" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("revenue");
        // the same amount again changes nothing
        (await (await Client.PutJsonAsync(url, new { revenue = 5000m, description = "Same" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("revenue");

        (await HistoryAsync(project.Id, superAdmin.AccessToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Internal_work_has_no_payment_and_turning_into_it_says_where_the_payment_went()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5000m);

        var internalWork = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing", new { billingType = "internal" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        internalWork.Money!.Revenue.ShouldBe(0m);
        var made = (await HistoryAsync(project.Id, superAdmin.AccessToken)).First();
        (made.Amount, made.PreviousAmount, made.Description).ShouldBe((0m, 5000m, ProjectRules.MadeInternal));

        (await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 100m, description = "Billable after all" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("revenue");

        // client work again: it starts at no payment, which is then set with why
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var clientWork = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing", new { billingType = "client", clientId = client.Id }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        clientWork.Money!.Revenue.ShouldBe(0m);
        (await HistoryAsync(project.Id, superAdmin.AccessToken)).Count.ShouldBe(2);
        await PayAsync(project.Id, 3000m, "Billed to the new client", superAdmin.AccessToken);
        var paid = (await HistoryAsync(project.Id, superAdmin.AccessToken)).First();
        (paid.Amount, paid.PreviousAmount).ShouldBe((3000m, (decimal?)0m));

        // an internal project from the start has no history
        var other = await (await Client.PostJsonAsync("/api/projects", new { title = "Office fit-out", domainCode = DomainCodes.Design, billingType = "internal" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        (await HistoryAsync(other.Id, superAdmin.AccessToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_execution_fee_and_markup_change_keeps_both_and_why()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var url = $"/api/projects/{site.Id}/pricing";

        var opening = (await HistoryAsync(site.Id, superAdmin.AccessToken)).ShouldHaveSingleItem();
        (opening.Amount, opening.PreviousAmount, opening.MaterialMarkup, opening.Description).ShouldBe((1000m, null, (decimal?)0.10m, ProjectRules.AgreedAtStart));

        (await (await Client.PutJsonAsync(url, new { projectFee = 1500m, materialMarkup = 0.10m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("description");
        (await (await Client.PutJsonAsync(url, new { projectFee = 1000m, materialMarkup = 0.10m, description = "Same" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("projectFee");

        var priced = await (await Client.PutJsonAsync(url, new { projectFee = 1500m, materialMarkup = 0.10m, description = "An extra floor" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        priced.Money!.ProjectFee.ShouldBe(1500m);
        // the markup alone is a change too
        await (await Client.PutJsonAsync(url, new { projectFee = 1500m, materialMarkup = 0.15m, description = "Imported tiles" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();

        var history = await HistoryAsync(site.Id, superAdmin.AccessToken);
        history.Count.ShouldBe(3);
        (history[0].Amount, history[0].PreviousAmount, history[0].MaterialMarkup, history[0].PreviousMaterialMarkup, history[0].Description)
            .ShouldBe((1500m, (decimal?)1500m, (decimal?)0.15m, (decimal?)0.10m, "Imported tiles"));
        (history[1].Amount, history[1].PreviousAmount, history[1].MaterialMarkup, history[1].Description)
            .ShouldBe((1500m, (decimal?)1000m, (decimal?)0.10m, "An extra floor"));

        // an Execution site's payment is its fee: the Design payment doesn't apply
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/payment", new { revenue = 5000m, description = "X" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task A_finished_project_keeps_its_payment_and_its_history_can_still_be_read()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5000m);
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 6000m, description = "Late extra" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        (await HistoryAsync(project.Id, superAdmin.AccessToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_history_is_for_people_who_see_the_money_of_a_project_they_can_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5000m);
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, executionMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        (await HistoryAsync(project.Id, designMoney.AccessToken)).ShouldHaveSingleItem();
        await (await Client.GetAsync($"/api/projects/{project.Id}/payment-changes", executionMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{project.Id}/payment-changes", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 1m, description = "Mine" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/projects/999999999/payment-changes", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
