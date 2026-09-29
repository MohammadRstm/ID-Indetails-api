using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Realtime;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class PresenceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Using_the_app_updates_last_seen_and_nobody_is_online_without_a_live_connection()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.ScalarAsync<int>(
            "UPDATE users SET last_seen_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 2 DAY) WHERE id = @id; SELECT 1", ("@id", designer.User.Id));

        var response = await Client.GetAsync("/api/presence", designer.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        (await response.ReadAsync<PresenceResponse>()).Online.ShouldBeEmpty();

        var seen = await _steps.ScalarAsync<DateTime>("SELECT last_seen_at FROM users WHERE id = @id", ("@id", designer.User.Id));
        (DateTime.UtcNow - seen).ShouldBeLessThan(TimeSpan.FromMinutes(1));

        var profile = await (await Client.GetAsync($"/api/employees/{designer.User.Id}", superAdmin.AccessToken)).ReadAsync<EmployeeResponse>();
        profile.Online.ShouldBeFalse();
        profile.LastSeenAt.ShouldNotBeNull();

        await (await Client.GetAsync("/api/presence")).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }
}
