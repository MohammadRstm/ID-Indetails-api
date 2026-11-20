using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A pay change that would take a running client project's profit to zero or below is held for the super admins.
/// June 2026: 1,760 a month is 10 an hour, 2,112 is 12. A 9-to-5 day on Monday 22 June costs 9 paid hours (with the
/// paid break): 90 at 10, 108 at 12, so a project paid 100 goes from 10 of profit to -8.
/// </summary>
public class PayHoldTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => _steps.ScalarAsync<long>(sql, parameters);

    private Task<long> NotifiedAsync(string kind, AuthResponse to)
        => CountAsync("SELECT COUNT(*) FROM notifications WHERE kind = @kind AND recipient_id = @to", ("@kind", kind), ("@to", to.User.Id));

    /// <summary>A designer on 1,760 since January with a day's work on a client project paid 100.</summary>
    private async Task<(AuthResponse Designer, ProjectResponse Project)> NearTheFloorAsync(AuthResponse superAdmin)
    {
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(designer.User.Id, "2026-01-01");
        await OkAsync<PayResponse>(Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { amount = 1760m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken));
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 100m);
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-06-22 06:00:00', '2026-06-22 14:00:00', UTC_TIMESTAMP(3))", connection);
        command.Parameters.AddWithValue("@u", designer.User.Id);
        command.Parameters.AddWithValue("@p", project.Id);
        await command.ExecuteNonQueryAsync();
        return (designer, project);
    }

    private Task<HttpResponseMessage> RaiseAsync(long userId, AuthResponse by)
        => Client.PostJsonAsync($"/api/employees/{userId}/pay", new { amount = 2112m, currency = "USD", effectiveFrom = "2026-06-15" }, by.AccessToken);

    [Fact]
    public async Task A_raise_past_the_floor_is_held_and_only_a_super_admin_forces_it_through()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (designer, project) = await NearTheFloorAsync(superAdmin);
        var told = await NotifiedAsync(NotificationKinds.PayChanged, designer);

        var error = await (await RaiseAsync(designer.User.Id, money)).ShouldFailWithAsync(422, ErrorCodes.PayChangeHeld);
        var id = error.BlockedChangeId.ShouldNotBeNull();

        // nothing landed: the pay is as it was, and the employee wasn't told
        (await OkAsync<List<PayResponse>>(Client.GetAsync($"/api/employees/{designer.User.Id}/pay", superAdmin.AccessToken)))
            .ShouldHaveSingleItem().Amount.ShouldBe(1760m);
        (await NotifiedAsync(NotificationKinds.PayChanged, designer)).ShouldBe(told);
        // the super admins are told, with no amount; whoever else sees the money isn't, and can't open or decide it
        (await CountAsync("SELECT COUNT(*) FROM notifications WHERE kind = @k AND subject_id = @id AND recipient_id = @to",
            ("@k", NotificationKinds.PayChangeHeld), ("@id", id), ("@to", superAdmin.User.Id))).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) FROM notifications WHERE kind = @k AND subject_id = @id AND payload LIKE '%2112%'",
            ("@k", NotificationKinds.PayChangeHeld), ("@id", id))).ShouldBe(0);
        (await NotifiedAsync(NotificationKinds.ChangeBlocked, money)).ShouldBe(0);
        await (await Client.GetAsync($"/api/blocked-changes/{id}", money.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", money.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var held = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", superAdmin.AccessToken));
        held.Kind.ShouldBe(BlockedChangeKind.PayChange);
        held.ProjectId.ShouldBe(project.Id);
        held.Person!.Id.ShouldBe(designer.User.Id);
        held.Summary.ShouldContain("2,112.00 USD a month from 15 Jun 2026");
        var reached = held.Projects.ShouldNotBeNull().ShouldHaveSingleItem();
        reached.ProfitNow.ShouldBe(10m);
        reached.ProfitIfForced.ShouldBe(-8m);

        var forced = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        forced.Status.ShouldBe(BlockedChangeStatus.Forced);
        forced.DecidedBy!.Id.ShouldBe(superAdmin.User.Id);
        // the change is the requester's, and only now is the employee told
        var raise = (await OkAsync<List<PayResponse>>(Client.GetAsync($"/api/employees/{designer.User.Id}/pay", superAdmin.AccessToken)))
            .Single(p => p.Amount == 2112m);
        raise.CreatedById.ShouldBe(money.User.Id);
        (await NotifiedAsync(NotificationKinds.PayChanged, designer)).ShouldBe(told + 1);
    }

    [Fact]
    public async Task A_hold_made_before_the_pay_changed_again_can_only_be_dismissed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, money) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (designer, _) = await NearTheFloorAsync(superAdmin);
        var id = (await (await RaiseAsync(designer.User.Id, money)).ShouldFailWithAsync(422, ErrorCodes.PayChangeHeld)).BlockedChangeId.ShouldNotBeNull();

        // a change from July touches no work, so it lands, and the hold no longer fits
        await OkAsync<PayResponse>(Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { amount = 1800m, currency = "USD", effectiveFrom = "2026-07-01" }, superAdmin.AccessToken));

        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PayChangeStale);
        (await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/dismiss", superAdmin.AccessToken)))
            .Status.ShouldBe(BlockedChangeStatus.Dismissed);
        (await OkAsync<List<PayResponse>>(Client.GetAsync($"/api/employees/{designer.User.Id}/pay", superAdmin.AccessToken)))
            .Select(p => p.Amount).ShouldBe([1800m, 1760m]);
    }
}
