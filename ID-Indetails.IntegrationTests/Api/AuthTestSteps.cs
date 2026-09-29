using System.Net.Http.Json;
using ID_Indetails.api.DTO.Response.Auth;
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

    /// <summary>Invites someone as <paramref name="inviter"/> and completes their signup.</summary>
    public async Task<(string Email, string Password, AuthResponse Auth)> InviteAndAcceptAsync(
        AuthResponse inviter, string role, string? domain)
    {
        var email = NewEmail(role);
        var invite = await Client.PostJsonAsync("/api/invitations", new { email, role, domainCode = domain }, inviter.AccessToken);
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

    /// <summary>Puts someone in a team directly, without the fields the employee edit needs.</summary>
    public async Task PutInTeamAsync(long userId, long teamId)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand("UPDATE users SET team_id = @t WHERE id = @u", connection);
        command.Parameters.AddWithValue("@t", teamId);
        command.Parameters.AddWithValue("@u", userId);
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
}
