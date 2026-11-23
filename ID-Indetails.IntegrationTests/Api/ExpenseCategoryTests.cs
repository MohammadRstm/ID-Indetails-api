using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Each business's expense categories (23 November 2026). The tests share one database and categories are company-wide,
/// so each adds categories under a name of its own and removes them before it ends.
/// </summary>
public class ExpenseCategoryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<List<ExpenseCategoryResponse>> ListAsync(string domain, string token)
        => await (await Client.GetAsync($"/api/expense-categories?domain={domain}", token)).ReadAsync<List<ExpenseCategoryResponse>>();

    [Fact]
    public async Task Each_business_starts_with_the_categories_the_code_had()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = (await ListAsync(DomainCodes.Design, superAdmin.AccessToken)).Select(c => c.Code).ToList();
        var execution = (await ListAsync(DomainCodes.Execution, superAdmin.AccessToken)).Select(c => c.Code).ToList();

        design.ShouldContain("software");
        new[] { "assets", "services", "contractor", "software", "travel", "hardware", "other" }.ShouldBeSubsetOf(design);
        new[] { "materials", "contractor", "equipment", "fees", "other" }.ShouldBeSubsetOf(execution);
        execution.ShouldNotContain("software");
        (await ListAsync(DomainCodes.Execution, superAdmin.AccessToken)).Single(c => c.Code == "other").Removable.ShouldBe(false);

        // a business without expense categories, and one the caller can't see
        await (await Client.GetAsync("/api/expense-categories?domain=website", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await (await Client.GetAsync("/api/expense-categories?domain=execution", designer.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ListAsync(DomainCodes.Design, designer.AccessToken)).ShouldAllBe(c => c.Removable == null);
    }

    [Fact]
    public async Task A_new_category_is_used_by_site_expenses_and_budgets_and_can_t_be_removed_while_in_use()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = $"Scaffolding {Guid.NewGuid().ToString("N")[..6]}";

        var added = await Client.PostJsonAsync("/api/expense-categories", new { domain = DomainCodes.Execution, name }, superAdmin.AccessToken);
        ((int)added.StatusCode).ShouldBe(201, await added.Content.ReadAsStringAsync());
        var category = await added.ReadAsync<ExpenseCategoryResponse>();
        category.Code.ShouldStartWith("scaffolding_");
        category.Removable.ShouldBe(true);
        await (await Client.PostJsonAsync("/api/expense-categories", new { domain = DomainCodes.Execution, name = name.ToUpperInvariant() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ExpenseCategoryNameTaken);

        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 50_000m);
        var expense = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Tower hire", amount = 400m, category = category.Code } } }, superAdmin.AccessToken);
        ((int)expense.StatusCode).ShouldBe(201, await expense.Content.ReadAsStringAsync());
        var expenseId = (await expense.ReadAsync<List<SiteExpenseResponse>>()).Single().Id;
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
                new { items = new[] { new { name = "Mystery", amount = 10m, category = "software" } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // a site's budget has a line for it too
        (await Client.PutJsonAsync($"/api/projects/{site.Id}/budget",
                new { reason = "Scaffolding", lines = new object[] { new { category = category.Code, amount = 500m } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        // in use: blocked; renamed freely; "other" never goes
        await (await Client.DeleteAsync($"/api/expense-categories/{category.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExpenseCategoryInUse);
        var renamed = await (await Client.PutJsonAsync($"/api/expense-categories/{category.Id}", new { name = name + " towers" }, superAdmin.AccessToken))
            .ReadAsync<ExpenseCategoryResponse>();
        renamed.Code.ShouldBe(category.Code);
        renamed.Name.ShouldBe(name + " towers");
        var other = (await ListAsync(DomainCodes.Execution, superAdmin.AccessToken)).Single(c => c.Code == "other");
        await (await Client.DeleteAsync($"/api/expense-categories/{other.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExpenseCategoryProtected);

        // once nothing uses it, it goes
        (await Client.PutJsonAsync($"/api/projects/{site.Id}/budget",
                new { reason = "Materials instead", lines = new object[] { new { category = "materials", amount = 500m } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await _steps.ExecuteAsync("UPDATE project_budget_lines SET category = 'materials' WHERE category = @c", ("@c", category.Code));
        (await Client.DeleteAsync($"/api/projects/{site.Id}/site-expenses/{expenseId}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.DeleteAsync($"/api/expense-categories/{category.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await ListAsync(DomainCodes.Execution, superAdmin.AccessToken)).ShouldNotContain(c => c.Id == category.Id);
    }

    [Fact]
    public async Task Only_whoever_changes_the_money_changes_the_categories()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await (await Client.PostJsonAsync("/api/expense-categories", new { domain = DomainCodes.Design, name = "Printing" }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // a Design project expense takes only Design's categories
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
                new { label = "Cement", amount = 10m, currency = "USD", category = "materials" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
                new { label = "Licence", amount = 10m, currency = "USD", category = "software" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }
}
