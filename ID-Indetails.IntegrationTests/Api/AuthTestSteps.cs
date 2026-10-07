using System.Net.Http.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Teams;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reusable steps: create a super admin, invite and sign people up, query the database.</summary>
public sealed class AuthTestSteps(ApiFactory api)
{
    public HttpClient Client { get; } = api.CreateApiClient();

    public static string NewEmail(string prefix = "user") => $"{prefix}.{Guid.NewGuid():N}@test.local";

    public Task<HttpResponseMessage> CallSetupAsync(object body, string? setupKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/setup/super-admin")
        {
            Content = JsonContent.Create(body, options: ApiClientExtensions.Json),
        };
        if (setupKey is not null)
        {
            request.Headers.Add("X-Setup-Key", setupKey);
        }

        return Client.SendAsync(request);
    }

    /// <summary>A super admin who has already replaced the temporary password.</summary>
    public async Task<(string Email, string Password, AuthResponse Auth)> ActiveSuperAdminAsync()
    {
        var email = NewEmail("super");
        (await CallSetupAsync(new { email, firstName = "Rana", lastName = "Haddad" }, ApiFactory.SetupKey))
            .IsSuccessStatusCode.ShouldBeTrue();

        var temporary = api.Emails.TemporaryPasswordFor(email);
        var first = await (await Client.PostJsonAsync("/api/auth/login", new { email, password = temporary })).ShouldSignInAsync();

        const string password = "super-admin-own-passphrase";
        var auth = await (await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = temporary, newPassword = password },
            first.AccessToken)).ShouldSignInAsync();

        return (email, password, auth);
    }

    /// <summary>A new team of <paramref name="domain"/>, made by a super admin (a new company has none), its name made unique.</summary>
    public async Task<TeamResponse> CreateTeamAsync(AuthResponse superAdmin, string domain, string name)
    {
        var response = await Client.PostJsonAsync("/api/teams",
            new { name = $"{name} {Guid.NewGuid().ToString("N")[..6]}", domainCode = domain }, superAdmin.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TeamResponse>();
    }

    /// <summary>
    /// Invites someone as <paramref name="inviter"/> and completes their signup. Admins and super admins must be
    /// invited with a salary: without <paramref name="pay"/> they get 2,000 USD a month from today.
    /// </summary>
    public async Task<(string Email, string Password, AuthResponse Auth)> InviteAndAcceptAsync(
        AuthResponse inviter, string role, string? domain, object? pay = null)
    {
        var email = NewEmail(role);
        pay ??= role == RoleCodes.Employee ? null : new { basis = "monthly", amount = 2000m, currency = "USD" };
        var invite = await Client.PostJsonAsync("/api/invitations", new { email, role, domainCode = domain, pay }, inviter.AccessToken);
        ((int)invite.StatusCode).ShouldBe(201, await invite.Content.ReadAsStringAsync());

        const string password = "invited-user-passphrase";
        var auth = await (await Client.PostJsonAsync("/api/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(email),
            firstName = "Test",
            lastName = role,
            password,
        })).ShouldSignInAsync();

        return (email, password, auth);
    }

    /// <summary>Money and hiring: by default only super admins hold these.</summary>
    public static readonly string[] MoneyPermissions =
    [
        PermissionCodes.FinancialView, PermissionCodes.InvoiceCreate, PermissionCodes.InvoiceSend, PermissionCodes.InvoiceVoid,
        PermissionCodes.PaymentRecord, PermissionCodes.PaymentVoid, PermissionCodes.BillingSettingsManage, PermissionCodes.ExpenseManage,
        PermissionCodes.ProjectPricingManage, PermissionCodes.StatementCreate, PermissionCodes.StatementSend,
        PermissionCodes.EmployeeInvite, PermissionCodes.EmployeePayManage,
    ];

    /// <summary>
    /// An admin of <paramref name="domain"/> whom the super admin has also granted the <see cref="MoneyPermissions"/> of that
    /// domain. The other domain's ones are left out: holding one would open that domain to them.
    /// </summary>
    public async Task<(string Email, string Password, AuthResponse Auth)> MoneyAdminAsync(AuthResponse superAdmin, string domain)
    {
        var admin = await InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, domain);
        var codes = PermissionCatalog.Definitions
            .Where(d => MoneyPermissions.Contains(d.Code) && (d.DomainCode is null || d.DomainCode == domain))
            .Select(d => d.Code);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.Auth.User.Id}/permissions",
            new { changes = codes.Select(code => new { code, action = "grant", reason = "Handles the money" }) }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }

    /// <summary>Puts someone in a team directly, without the fields the employee edit needs.</summary>
    public Task PutInTeamAsync(long userId, long teamId)
        => ExecuteAsync("UPDATE users SET team_id = @t WHERE id = @u", ("@t", teamId), ("@u", userId));

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    /// <summary>Moves someone's joining day, and the start of their first period of employment with it.</summary>
    public Task SetJoinedOnAsync(long userId, string day)
        => ScalarAsync<int>(
            "UPDATE users SET joined_on = @day WHERE id = @id; " +
            "UPDATE employment_periods SET started_on = @day WHERE user_id = @id ORDER BY started_on LIMIT 1; SELECT 1",
            ("@id", userId), ("@day", day));

    /// <summary>Moves when someone who was deactivated left: their account's deactivation time and last working day.</summary>
    public Task SetLeftAtAsync(long userId, string utc)
        => ScalarAsync<int>(
            "UPDATE users SET deactivated_at = @at WHERE id = @id; " +
            "UPDATE employment_periods SET ended_on = DATE(@at) WHERE user_id = @id ORDER BY started_on DESC LIMIT 1; SELECT 1",
            ("@id", userId), ("@at", utc));
}
