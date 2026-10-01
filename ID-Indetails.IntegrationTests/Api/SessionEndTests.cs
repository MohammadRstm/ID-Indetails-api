using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Employees;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>A super admin ending someone's sessions, for an account someone else is using, and restoring their sign-in.</summary>
public class SessionEndTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Ended_sessions_stop_at_once_and_nobody_signs_in_until_a_super_admin_restores_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, signedUp) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var id = signedUp.User.Id;
        // the person, and whoever else is signed in as them
        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password });
        var refresh = login.RefreshCookie();
        var stolen = (await login.ShouldSignInAsync()).AccessToken;
        (await Client.GetAsync("/api/auth/me", stolen)).IsSuccessStatusCode.ShouldBeTrue();

        var ended = await Client.PostEmptyAsync($"/api/employees/{id}/sessions/end", superAdmin.AccessToken);
        ended.IsSuccessStatusCode.ShouldBeTrue(await ended.Content.ReadAsStringAsync());
        (await ended.ReadAsync<EmployeeResponse>()).SessionsRevokedAt.ShouldNotBeNull();

        // every way in is shut: the token they hold, a refresh, and signing in with the right password
        await (await Client.GetAsync("/api/auth/me", stolen)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: refresh)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldFailWithAsync(403, ErrorCodes.SignInSuspended);
        // a wrong password still says only that, so the suspension tells a stranger nothing
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "not-their-password" })).ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await Client.PostEmptyAsync($"/api/employees/{id}/sessions/end", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SessionsAlreadyEnded);

        var restored = await Client.PostEmptyAsync($"/api/employees/{id}/sessions/restore", superAdmin.AccessToken);
        restored.IsSuccessStatusCode.ShouldBeTrue(await restored.Content.ReadAsStringAsync());
        (await restored.ReadAsync<EmployeeResponse>()).SessionsRevokedAt.ShouldBeNull();

        // the old token stays dead; a fresh sign-in works (a token's time is whole seconds, so it comes a second later)
        await (await Client.GetAsync("/api/auth/me", stolen)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var fresh = await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldSignInAsync();
        (await Client.GetAsync("/api/auth/me", fresh.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostEmptyAsync($"/api/employees/{id}/sessions/restore", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SessionsNotEnded);
    }

    [Fact]
    public async Task Only_a_super_admin_ends_sessions_and_never_their_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.PostEmptyAsync($"/api/employees/{employee.User.Id}/sessions/end", admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        await (await Client.PostEmptyAsync($"/api/employees/{superAdmin.User.Id}/sessions/end", superAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        (await Client.GetAsync("/api/auth/me", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }
}
