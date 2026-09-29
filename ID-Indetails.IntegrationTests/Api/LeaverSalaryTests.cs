using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Payroll;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// People whose account was deactivated stay in the salaries of the months they were active, with their final month
/// pro-rated to the day they left. June 2026 has 22 working days, 11 of them up to Monday 15 June.
/// </summary>
public class LeaverSalaryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Deactivating_and_reactivating_sets_and_clears_the_leaving_time()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await SalariedAsync(superAdmin);

        (await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<int>("SELECT deactivated_at IS NOT NULL FROM users WHERE id = @id", ("@id", person.User.Id))).ShouldBe(1);

        (await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/reactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<int>("SELECT deactivated_at IS NULL FROM users WHERE id = @id", ("@id", person.User.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_leaver_is_paid_for_their_final_month_up_to_the_day_they_left_and_not_after()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var leaver = await SalariedAsync(superAdmin);
        await DeactivateAsync(superAdmin, leaver, "2026-06-15 12:00:00");

        var june = (await RowAsync(superAdmin, leaver, "2026-06")).ShouldNotBeNull();
        june.ShouldSatisfyAllConditions(
            r => r.LeftOn.ShouldBe(new DateOnly(2026, 6, 15)),
            r => r.ProRated.ShouldBeTrue(),
            r => r.WorkingDays.ShouldBe(22),
            r => r.WorkingDaysPaid.ShouldBe(11),
            r => r.ExpectedMinutes.ShouldBe(11 * 420));

        var detail = await Client.GetAsync($"/api/company/salaries/{leaver.User.Id}?month=2026-06", superAdmin.AccessToken);
        ((int)detail.StatusCode).ShouldBe(200, await detail.Content.ReadAsStringAsync());
        (await detail.ReadAsync<SalaryDetailResponse>()).MissedDays.Count.ShouldBe(11);

        var csv = await (await Client.GetAsync($"/api/company/salaries/export?month=2026-06&search={leaver.User.Email}", superAdmin.AccessToken))
            .Content.ReadAsStringAsync();
        csv.ShouldContain(leaver.User.Email);
        csv.ShouldContain("11/22 days");

        (await RowAsync(superAdmin, leaver, "2026-05")).ShouldNotBeNull().ProRated.ShouldBeFalse();
        (await RowAsync(superAdmin, leaver, "2026-07")).ShouldBeNull();
        await (await Client.GetAsync($"/api/company/salaries/{leaver.User.Id}?month=2026-07", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Someone_deactivated_without_a_known_date_is_left_out_as_before()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await SalariedAsync(superAdmin);
        await DeactivateAsync(superAdmin, person, deactivatedAtUtc: null);

        (await RowAsync(superAdmin, person, "2026-06")).ShouldBeNull();
    }

    [Fact]
    public async Task Other_domains_never_see_a_leaver()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var leaver = await SalariedAsync(superAdmin);
        await DeactivateAsync(superAdmin, leaver, "2026-06-15 12:00:00");
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        (await Client.PutJsonAsync($"/api/users/{executionAdmin.User.Id}/permissions",
                new { changes = new[] { new { code = PermissionCodes.SalaryView, action = "grant" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        (await RowAsync(executionAdmin, leaver, "2026-06")).ShouldBeNull();
        await (await Client.GetAsync($"/api/company/salaries/{leaver.User.Id}?month=2026-06", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    /// <summary>A Design employee who joined on 1 January 2026 with 1,733.30 a month (10 an hour).</summary>
    private async Task<AuthResponse> SalariedAsync(AuthResponse superAdmin)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ExecuteAsync("UPDATE users SET joined_on = '2026-01-01' WHERE id = @id", ("@id", person.User.Id));
        (await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
                new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        return person;
    }

    /// <summary>Deactivates through the API, then moves the leaving time into the past (UTC), or clears it.</summary>
    private async Task DeactivateAsync(AuthResponse superAdmin, AuthResponse person, string? deactivatedAtUtc)
    {
        (await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await ExecuteAsync("UPDATE users SET deactivated_at = @at WHERE id = @id",
            ("@at", deactivatedAtUtc is null ? DBNull.Value : DateTime.Parse(deactivatedAtUtc)), ("@id", person.User.Id));
    }

    private async Task<SalaryRowResponse?> RowAsync(AuthResponse viewer, AuthResponse person, string month)
    {
        var response = await Client.GetAsync($"/api/company/salaries?month={month}&search={person.User.Email}", viewer.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<SalariesResponse>()).Rows.Items.SingleOrDefault(r => r.UserId == person.User.Id);
    }

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
}
