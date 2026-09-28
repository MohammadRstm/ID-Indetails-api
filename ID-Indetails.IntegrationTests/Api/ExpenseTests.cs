using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ExpenseTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Admin_adds_and_removes_expenses_with_totals_per_currency()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var created = await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Render farm", amount = 120.50m, currency = "usd", category = "software", spentOn = "2026-09-01" }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var licence = await created.ReadAsync<ExpenseResponse>();
        licence.Currency.ShouldBe("USD");

        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Taxi", amount = 900_000m, currency = "LBP" }, admin.AccessToken);
        var printing = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Printing", amount = 30m, currency = "USD" }, admin.AccessToken)).ReadAsync<ExpenseResponse>();
        printing.Category.ShouldBe("other");

        (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{printing.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);

        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", admin.AccessToken)).ReadAsync<ExpenseListResponse>();
        list.Items.Count.ShouldBe(2);
        list.Totals.ShouldBe([new CurrencyTotalResponse("LBP", 900_000m), new CurrencyTotalResponse("USD", 120.50m)]);

        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.ExpenseTotals.Count.ShouldBe(2);

        await (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{printing.Id}", admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND subject_type = 'expense'", ("@p", project.Id))).ShouldBe(4);
    }

    [Fact]
    public async Task Finished_projects_take_no_expense_changes_until_reopened()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var expense = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Travel", amount = 50m, currency = "USD" }, superAdmin.AccessToken)).ReadAsync<ExpenseResponse>();

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Late", amount = 5m, currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{expense.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{expense.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_reach_expenses()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "X", amount = 1m, currency = "USD" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "X", amount = 1m, currency = "USD" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
