using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Payroll;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>June 2026 under the seeded 9–5 week; Beirut is UTC+3 in June.</summary>
public class ProjectPayTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<AuthResponse> PaidDesignerAsync(AuthResponse superAdmin, string basis, decimal amount)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        (await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
            new { basis, amount, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        return person;
    }

    private Task LogAsync(long userId, long projectId, string day)
        => ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Work', @start, @end, UTC_TIMESTAMP(3))",
            ("@u", userId), ("@p", projectId), ("@start", $"{day} 06:00:00"), ("@end", $"{day} 14:00:00"));

    [Fact]
    public async Task Each_project_pays_its_hours_at_each_persons_rate_without_breaks()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var salaried = await PaidDesignerAsync(superAdmin, "monthly", 1760m); // 10 an hour in June (176 paid hours)
        var hourly = await PaidDesignerAsync(superAdmin, "hourly", 20m);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // An 8-hour day each (7h in schedule and 1h over). Both are far short of their June hours, so the
        // extra hour makes up the shortfall and is paid at the plain rate; no paid break is added.
        await LogAsync(salaried.User.Id, project.Id, "2026-06-01");
        await LogAsync(hourly.User.Id, project.Id, "2026-06-02");

        var response = await Client.GetAsync("/api/company/project-pay?unit=month&date=2026-06-15", superAdmin.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var report = await response.ReadAsync<ProjectPayResponse>();

        report.From.ShouldBe(new DateOnly(2026, 6, 1));
        report.To.ShouldBe(new DateOnly(2026, 6, 30));
        var group = report.Projects.ShouldHaveSingleItem();
        group.ProjectId.ShouldBe(project.Id);
        group.People.Single(p => p.UserId == salaried.User.Id).ShouldSatisfyAllConditions(
            p => p.Pay.ShouldBe(80m),
            p => p.MonthlySalary.ShouldBe(1760m));
        group.People.Single(p => p.UserId == hourly.User.Id).ShouldSatisfyAllConditions(
            p => p.Pay.ShouldBe(160m),
            p => p.MonthlySalary.ShouldBeNull());
        group.NormalMinutes.ShouldBe(2 * 480);
        report.Totals.ShouldHaveSingleItem().Total.ShouldBe(240m);

        var csv = await (await Client.GetAsync("/api/company/project-pay/export?unit=month&date=2026-06-15", superAdmin.AccessToken))
            .Content.ReadAsStringAsync();
        csv.ShouldContain(project.Title);
        csv.ShouldContain("160.00");

        // The week of 8 June has no work.
        var week = await (await Client.GetAsync("/api/company/project-pay?unit=week&date=2026-06-10", superAdmin.AccessToken))
            .ReadAsync<ProjectPayResponse>();
        week.Projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Project_pay_needs_salary_view_and_never_shows_a_future_period()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.GetAsync("/api/company/project-pay", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/company/project-pay?date=2099-01-01", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ProjectPayPeriodInFuture);
        await (await Client.GetAsync("/api/company/project-pay?projectId=999999999", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
