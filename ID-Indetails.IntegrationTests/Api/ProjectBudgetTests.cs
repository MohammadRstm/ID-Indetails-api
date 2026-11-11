using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A project's cost budget: versions with their reasons, each line against the cost so far from the shared calculator.
/// Design's are set with financial:write, Execution's with project:pricing_manage; another domain's project reads as
/// not found.
/// </summary>
public class ProjectBudgetTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static string Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static string InDays(int days) => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).AddDays(days).ToString("yyyy-MM-dd");

    private static string Url(long projectId) => $"/api/projects/{projectId}/budget";

    /// <summary>A Design client project of 10,000 before TVA, with <paramref name="departmentIds"/>.</summary>
    private async Task<ProjectResponse> DesignProjectAsync(AuthResponse superAdmin, IReadOnlyList<long> departmentIds)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10000m,
            tvaRate = 0m,
            departmentIds,
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectBudgetResponse> SetAsync(AuthResponse who, long projectId, object body)
    {
        var response = await Client.PutJsonAsync(Url(projectId), body, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectBudgetResponse>();
    }

    private async Task<ProjectBudgetResponse> GetAsync(AuthResponse who, long projectId)
    {
        var response = await Client.GetAsync(Url(projectId), who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectBudgetResponse>();
    }

    [Fact]
    public async Task A_design_money_admin_sets_a_budget_and_reads_each_line_against_its_cost_and_a_revision_keeps_the_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure " + Guid.NewGuid().ToString("N")[..6]);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await DesignProjectAsync(superAdmin, [architecture, structure.Id]);

        // 1,000 USD of expenses, and 3 hours in Architecture on Tuesday 10 February 2026 by someone paid 1,600 USD a month
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
                new { label = "Structural survey", amount = 1_000m, currency = "USD", category = "other", spentOn = Today }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
                new { basis = "monthly", amount = 1_600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, team_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, @t, 'Drawings', '2026-02-10 07:00:00', '2026-02-10 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id), ("@t", architecture));

        // no budget yet: every cost is uncovered
        var none = await GetAsync(admin, project.Id);
        none.Current.ShouldBeNull();
        none.Lines.ShouldBeEmpty();
        none.History.ShouldBeEmpty();
        none.Domain.ShouldBe(DomainCodes.Design);

        var first = await SetAsync(admin, project.Id, new
        {
            reason = "Agreed scope",
            lines = new object[]
            {
                new { teamId = architecture, amount = 5_000m },
                new { teamId = structure.Id, amount = 2_000m },
                new { category = "expenses", amount = 1_500m },
            },
        });

        // Architecture's cost is its labour, as the departments window counts it
        var departments = await (await Client.GetAsync($"/api/projects/{project.Id}/departments", admin.AccessToken)).ReadAsync<ProjectDepartmentsResponse>();
        var labour = departments.Departments.Single(d => d.Id == architecture).Money!.Cost;
        labour.ShouldBeGreaterThan(0m);
        none.Uncovered.ShouldBe(labour + 1_000m);

        first.Current.ShouldNotBeNull();
        first.Current.Version.ShouldBe(1);
        first.Current.Total.ShouldBe(8_500m);
        first.Current.Reason.ShouldBe("Agreed scope");
        first.Current.EffectiveFrom.ToString("yyyy-MM-dd").ShouldBe(Today);
        first.Current.SetByName.ShouldNotBeNullOrWhiteSpace();
        var architectureLine = first.Lines.Single(l => l.TeamId == architecture);
        (architectureLine.Category, architectureLine.Amount, architectureLine.Cost, architectureLine.Left).ShouldBe(((string?)null, 5_000m, labour, 5_000m - labour));
        var structureLine = first.Lines.Single(l => l.TeamId == structure.Id);
        (structureLine.Name, structureLine.Cost, structureLine.Left).ShouldBe((structure.Name, 0m, 2_000m));
        var expenses = first.Lines.Single(l => l.Category == "expenses");
        (expenses.Name, expenses.TeamId, expenses.Cost, expenses.Left).ShouldBe(("Expenses", (long?)null, 1_000m, 500m));
        // departments first, then the categories
        first.Lines.Last().Category.ShouldBe("expenses");
        first.Uncovered.ShouldBe(0m);
        first.Unconverted.ShouldBeEmpty();
        first.History.Select(h => h.Version).ShouldBe([1]);

        (await GetAsync(admin, project.Id)).ShouldBeEquivalentTo(first);

        // a revision without the expenses line, from the first of the month: version 2, and version 1 is kept
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var second = await SetAsync(admin, project.Id, new
        {
            reason = "Expenses come out of the fee",
            effectiveFrom = firstOfMonth.ToString("yyyy-MM-dd"),
            lines = new object[] { new { teamId = architecture, amount = 6_000m }, new { teamId = structure.Id, amount = 2_000m } },
        });
        second.Current!.Version.ShouldBe(2);
        second.Current.Total.ShouldBe(8_000m);
        second.Current.EffectiveFrom.ShouldBe(firstOfMonth);
        second.Lines.Count.ShouldBe(2);
        second.Uncovered.ShouldBe(1_000m);
        second.History.Select(h => (h.Version, h.Total, h.Reason)).ShouldBe([(2, 8_000m, "Expenses come out of the fee"), (1, 8_500m, "Agreed scope")]);

        // each version is in the activity, its total beside the last one, and no one in the payload
        (await _steps.ScalarAsync<long>(
                "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'budget_set'", ("@p", project.Id)))
            .ShouldBe(2);
        (await _steps.ScalarAsync<decimal>(
                "SELECT prev_amount FROM activity_events WHERE project_id = @p AND kind = 'budget_set' ORDER BY id DESC LIMIT 1", ("@p", project.Id)))
            .ShouldBe(8_500m);
        (await _steps.ScalarAsync<long>(
                "SELECT COUNT(*) FROM project_budget_lines l JOIN project_budgets b ON b.id = l.budget_id WHERE b.project_id = @p", ("@p", project.Id)))
            .ShouldBe(5);
    }

    [Fact]
    public async Task A_budget_line_must_fit_the_project_and_a_budget_needs_a_reason_and_a_day_that_has_come()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var elsewhere = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Interior " + Guid.NewGuid().ToString("N")[..6]);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await DesignProjectAsync(superAdmin, [architecture]);
        object Line(long teamId, decimal amount) => new { teamId, amount };

        await (await Client.PutJsonAsync(Url(project.Id), new { reason = "", lines = new[] { Line(architecture, 100m) } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync(Url(project.Id), new { reason = "Twice", lines = new[] { Line(architecture, 100m), Line(architecture, 200m) } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var notOnProject = await (await Client.PutJsonAsync(Url(project.Id),
                new { reason = "Interior too", lines = new[] { Line(architecture, 100m), Line(elsewhere.Id, 100m) } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.BudgetLineInvalid);
        notOnProject.Errors!.Keys.ShouldBe(["lines[1].teamId"]);
        var siteCategory = await (await Client.PutJsonAsync(Url(project.Id),
                new { reason = "Materials", lines = new object[] { new { category = "materials", amount = 100m } } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.BudgetLineInvalid);
        siteCategory.Errors!.Keys.ShouldBe(["lines[0].category"]);
        var future = await (await Client.PutJsonAsync(Url(project.Id),
                new { reason = "Next week", effectiveFrom = InDays(3), lines = new[] { Line(architecture, 100m) } }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        future.Errors!.Keys.ShouldBe(["effectiveFrom"]);

        // nothing was kept
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM project_budgets WHERE project_id = @p", ("@p", project.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task A_sites_budget_needs_pricing_and_another_domains_project_reads_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses", new
            {
                items = new object[]
                {
                    new { name = "Cement", amount = 400m, category = "materials" },
                    new { name = "Steel fixer", amount = 100m, category = "contractor" },
                },
            }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var body = new
        {
            reason = "Site plan",
            lines = new object[] { new { category = "materials", amount = 5_000m }, new { category = "ground_labour", amount = 2_000m } },
        };

        // a Design money admin doesn't see the site; an Execution admin without the money permissions is refused
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        await (await Client.PutJsonAsync(Url(site.Id), body, designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(Url(site.Id), designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var (_, _, plain) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        await (await Client.PutJsonAsync(Url(site.Id), body, plain.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(Url(site.Id), plain.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // whoever prices the site sets it: materials hold the cement, the contractor has no line
        var (_, _, executionMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var budget = await SetAsync(executionMoney, site.Id, body);
        budget.Domain.ShouldBe(DomainCodes.Execution);
        budget.Lines.Select(l => (l.Category, l.Name, l.Amount, l.Cost, l.Left)).ShouldBe(
        [
            ("materials", "Materials", 5_000m, 400m, 4_600m),
            ("ground_labour", "Ground labour", 2_000m, 0m, 2_000m),
        ]);
        budget.Lines.ShouldAllBe(l => l.TeamId == null);
        budget.Uncovered.ShouldBe(100m);

        // a site has no departments and no Design expenses line
        await (await Client.PutJsonAsync(Url(site.Id),
                new { reason = "Departments", lines = new object[] { new { teamId = await _steps.DesignDepartmentAsync(), amount = 100m } } },
                executionMoney.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.BudgetLineInvalid);
        await (await Client.PutJsonAsync(Url(site.Id),
                new { reason = "Expenses", lines = new object[] { new { category = "expenses", amount = 100m } } }, executionMoney.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.BudgetLineInvalid);

        // the super admin reads it too
        (await GetAsync(superAdmin, site.Id)).Current!.Version.ShouldBe(1);
    }
}
