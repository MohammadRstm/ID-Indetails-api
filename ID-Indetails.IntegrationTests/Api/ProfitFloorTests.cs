using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>A client project's profit never reaches zero unless someone who sees the money forces it there.</summary>
public class ProfitFloorTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<HttpResponseMessage> ExpenseAsync(long projectId, decimal amount, AuthResponse by)
        => Client.PostJsonAsync($"/api/projects/{projectId}/expenses",
            new { label = "Tiles", amount, currency = "USD", spentOn = DateTime.UtcNow.ToString("yyyy-MM-dd") }, by.AccessToken);

    private Task<HttpResponseMessage> SiteExpenseAsync(long projectId, decimal amount, AuthResponse by)
        => Client.PostJsonAsync($"/api/projects/{projectId}/site-expenses", new { items = new[] { new { name = "Crane hire", amount } } }, by.AccessToken);

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => _steps.ScalarAsync<long>(sql, parameters);

    private async Task<long> NotifiedAsync(string kind, long subjectId, AuthResponse to)
        => await CountAsync("SELECT COUNT(*) FROM notifications WHERE kind = @kind AND subject_id = @id AND recipient_id = @to",
            ("@kind", kind), ("@id", subjectId), ("@to", to.User.Id));

    [Fact]
    public async Task An_expense_that_would_take_the_profit_to_zero_is_kept_aside_and_forced_through_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 1000m);

        var error = await (await ExpenseAsync(project.Id, 1000m, superAdmin)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = error.BlockedChangeId.ShouldNotBeNull();
        (await CountAsync("SELECT COUNT(*) FROM project_expenses WHERE project_id = @p", ("@p", project.Id))).ShouldBe(0);
        (await NotifiedAsync(NotificationKinds.ChangeBlocked, id, money)).ShouldBe(1);
        (await NotifiedAsync(NotificationKinds.ChangeBlocked, id, superAdmin)).ShouldBe(0);

        var blocked = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", money.AccessToken));
        blocked.ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe(BlockedChangeStatus.Pending),
            b => b.Kind.ShouldBe(BlockedChangeKind.Expense),
            b => b.ProjectId.ShouldBe(project.Id),
            b => b.Summary.ShouldBe("Expense: Tiles, 1,000.00 USD"),
            b => b.RequestedBy.Id.ShouldBe(superAdmin.User.Id),
            b => b.ProfitNow.ShouldBe(1000m));

        var forced = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        forced.ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe(BlockedChangeStatus.Forced),
            b => b.DecidedBy!.Id.ShouldBe(superAdmin.User.Id),
            b => b.ProfitNow.ShouldBe(0m));
        (await CountAsync("SELECT COUNT(*) FROM project_expenses WHERE project_id = @p", ("@p", project.Id))).ShouldBe(1);
        (await NotifiedAsync(NotificationKinds.ProfitAtZero, project.Id, money)).ShouldBe(1);

        // once: forcing again, or dismissing it now, changes nothing
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", money.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BlockedChangeDecided);
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/dismiss", money.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BlockedChangeDecided);
        (await CountAsync("SELECT COUNT(*) FROM project_expenses WHERE project_id = @p", ("@p", project.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Close_to_zero_alerts_lowering_the_payment_past_it_is_refused_and_raising_it_always_passes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 1000m);

        // 10% left: allowed, and whoever sees the money hears it once
        (await ExpenseAsync(project.Id, 900m, superAdmin)).IsSuccessStatusCode.ShouldBeTrue();
        (await ExpenseAsync(project.Id, 10m, superAdmin)).IsSuccessStatusCode.ShouldBeTrue();
        (await NotifiedAsync(NotificationKinds.ProfitLow, project.Id, money)).ShouldBe(1);

        var lower = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 800m, description = "Smaller scope" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var dismissed = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{lower.BlockedChangeId}/dismiss", money.AccessToken));
        dismissed.ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe(BlockedChangeStatus.Dismissed),
            b => b.Kind.ShouldBe(BlockedChangeKind.Payment),
            b => b.Summary.ShouldBe("Payment set to 800.00 USD"));
        (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken))).Money!.Revenue.ShouldBe(1000m);

        (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 1200m, description = "Extra room" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task An_employees_hours_past_the_floor_are_forced_by_whoever_sees_the_money_and_stay_the_employees()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5m);

        var error = await (await Work.TryLogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 120))
            .ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = error.BlockedChangeId.ShouldNotBeNull();
        (await CountAsync("SELECT COUNT(*) FROM work_entries WHERE project_id = @p", ("@p", project.Id))).ShouldBe(0);
        (await NotifiedAsync(NotificationKinds.ChangeBlocked, id, superAdmin)).ShouldBe(1);
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var forced = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        forced.Kind.ShouldBe(BlockedChangeKind.WorkEntryCreate);
        forced.DecidedBy!.Id.ShouldBe(superAdmin.User.Id);
        (await CountAsync("SELECT COUNT(*) FROM work_entries WHERE project_id = @p AND user_id = @u", ("@p", project.Id), ("@u", designer.User.Id)))
            .ShouldBe(1);
        (await CountAsync("SELECT result_id FROM blocked_changes WHERE id = @id", ("@id", id))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Internal_work_and_projects_with_no_payment_yet_are_not_held_to_the_floor()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var created = await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling " + Guid.NewGuid().ToString("N")[..6], domainCode = DomainCodes.Design, billingType = "internal" }, superAdmin.AccessToken);
        var studio = await created.ReadAsync<ProjectResponse>();
        var unpaid = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 0m);

        (await ExpenseAsync(studio.Id, 500m, superAdmin)).IsSuccessStatusCode.ShouldBeTrue();
        (await ExpenseAsync(unpaid.Id, 500m, superAdmin)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task An_execution_site_close_to_zero_asks_the_pocket_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        // agreed at 1,000: 950 of cost leaves 5%
        (await SiteExpenseAsync(site.Id, 950m, superAdmin)).IsSuccessStatusCode.ShouldBeTrue();
        (await CountAsync("SELECT COUNT(*) FROM pocket_requests WHERE project_id = @p AND status = 'pending' AND requested_by_id IS NULL", ("@p", site.Id)))
            .ShouldBe(1);
        await (await SiteExpenseAsync(site.Id, 50m, superAdmin)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
    }

    [Fact]
    public async Task A_site_expense_that_would_take_the_profit_to_zero_is_kept_aside_and_forced_through_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var error = await (await SiteExpenseAsync(site.Id, 1000m, superAdmin)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = error.BlockedChangeId.ShouldNotBeNull();
        (await CountAsync("SELECT COUNT(*) FROM site_expenses WHERE project_id = @p", ("@p", site.Id))).ShouldBe(0);
        (await NotifiedAsync(NotificationKinds.ChangeBlocked, id, money)).ShouldBe(1);

        // stored as it always was (material_add), and said as an expense
        var blocked = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", money.AccessToken));
        blocked.ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BlockedChangeKind.MaterialAdd),
            b => b.Summary.ShouldBe("1 expense: Crane hire"),
            b => b.ProfitNow.ShouldBe(1000m));
        (await CountAsync("SELECT COUNT(*) FROM blocked_changes WHERE id = @id AND kind = 'material_add'", ("@id", id))).ShouldBe(1);

        var forced = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        forced.ProfitNow.ShouldBe(0m);
        (await CountAsync("SELECT COUNT(*) FROM site_expenses WHERE project_id = @p", ("@p", site.Id))).ShouldBe(1);
        (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", money.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.UnprocessableEntity);
        (await CountAsync("SELECT COUNT(*) FROM site_expenses WHERE project_id = @p", ("@p", site.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Raising_a_site_expense_past_the_floor_is_kept_aside_and_lowering_it_always_passes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var expense = (await OkAsync<List<ID_Indetails.api.DTO.Response.SiteExpenses.SiteExpenseResponse>>(SiteExpenseAsync(site.Id, 100m, superAdmin))).Single();
        var url = $"/api/projects/{site.Id}/site-expenses/{expense.Id}";

        var error = await (await Client.PutJsonAsync(url, new { name = "Crane hire", amount = 1000m }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = error.BlockedChangeId.ShouldNotBeNull();
        var blocked = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", money.AccessToken));
        blocked.ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BlockedChangeKind.MaterialUpdate),
            b => b.Summary.ShouldBe("Expense change: Crane hire"),
            b => b.ProfitNow.ShouldBe(900m));
        (await CountAsync("SELECT CAST(total AS SIGNED) FROM site_expenses WHERE id = @id", ("@id", expense.Id))).ShouldBe(100);

        // forcing makes the change it kept, once
        await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        (await CountAsync("SELECT CAST(total AS SIGNED) FROM site_expenses WHERE id = @id", ("@id", expense.Id))).ShouldBe(1000);

        // a lower cost raises the profit, which always passes
        (await Client.PutJsonAsync(url, new { name = "Crane hire", amount = 200m }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }
}
