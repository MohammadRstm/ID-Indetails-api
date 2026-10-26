using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The client portal's accounts: invitations to a Design project, the client's own password, their own token scheme
/// that never crosses with the staff one, and access given and ended per project.
/// </summary>
public class PortalTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string PortalCookie = "idi_portal_refresh";
    private const string Password = "client-own-passphrase";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record Setup(AuthResponse SuperAdmin, long ClientId, string ContactEmail, ProjectResponse Project);

    /// <summary>A super admin, a Design client with one email contact, and a project for that client.</summary>
    private async Task<Setup> DesignProjectAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var contact = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = contact, label = "Office" } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        var project = await ProjectForAsync(superAdmin, client.Id, DomainCodes.Design);
        return new Setup(superAdmin, client.Id, contact, project);
    }

    private async Task<ProjectResponse> ProjectForAsync(AuthResponse staff, long? clientId, string domain)
    {
        var title = "Project " + Guid.NewGuid().ToString("N")[..8];
        var body = domain == DomainCodes.Execution
            ? (object)new { title, domainCode = domain, clientId, staffIds = new[] { await new ProjectTestSteps(_steps).SiteStaffAsync() } }
            : clientId is null
                ? new { title, domainCode = domain, billingType = "internal" }
                : new { title, domainCode = domain, clientId };
        var response = await Client.PostJsonAsync("/api/projects", body, staff.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private sealed record IdOnly(long Id);

    private Task<HttpResponseMessage> InviteAsync(Setup s, string? email = null, AuthResponse? as_ = null, long? projectId = null)
        => Client.PostJsonAsync(
            $"/api/projects/{projectId ?? s.Project.Id}/portal/invitations", new { email = email ?? s.ContactEmail }, (as_ ?? s.SuperAdmin).AccessToken);

    private async Task<PortalClientsResponse> InvitedAsync(Setup s)
    {
        var response = await InviteAsync(s);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalClientsResponse>();
    }

    private async Task<(PortalAuthResponse Auth, string Cookie)> AcceptAsync(string token)
    {
        var response = await Client.PostJsonAsync("/api/portal/invitations/accept", new { token, firstName = "Rita", lastName = "Frem", password = Password });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<PortalAuthResponse>(), PortalCookieOf(response)!);
    }

    private async Task<(PortalAuthResponse Auth, string Cookie, Setup Setup)> ActiveClientAsync()
    {
        var s = await DesignProjectAsync();
        await InvitedAsync(s);
        var (auth, cookie) = await AcceptAsync(api.Emails.InvitationTokenFor(s.ContactEmail));
        return (auth, cookie, s);
    }

    private static string? PortalCookieOf(HttpResponseMessage response)
        => response.SetCookieHeaders()
            .Where(h => h.StartsWith(PortalCookie + "=", StringComparison.Ordinal))
            .Select(h => h[(PortalCookie.Length + 1)..].Split(';')[0])
            .LastOrDefault();

    private Task<HttpResponseMessage> PortalRefreshAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/portal/auth/refresh");
        request.Headers.Add("Cookie", $"{PortalCookie}={cookie}");
        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> MeAsync(string accessToken) => Client.GetAsync("/api/portal/me", accessToken);

    [Fact]
    public async Task A_client_is_invited_sets_their_own_password_and_sees_only_that_project()
    {
        var s = await DesignProjectAsync();
        var other = await ProjectForAsync(s.SuperAdmin, s.ClientId, DomainCodes.Design);

        var listed = await InvitedAsync(s);
        listed.Clients.Single().Status.ShouldBe(PortalClientStatus.Invited);

        // The email carries a one-time link and no password.
        var email = api.Emails.LastTo(s.ContactEmail);
        email.TextBody.ShouldContain(s.Project.Title);
        email.TextBody.ShouldNotContain("password:");
        var token = api.Emails.InvitationTokenFor(s.ContactEmail);

        var preview = await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token })).ReadAsync<PortalInvitationPreviewResponse>();
        preview.Email.ShouldBe(s.ContactEmail);
        preview.ProjectTitle.ShouldBe(s.Project.Title);

        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new { token, firstName = "Rita", lastName = "Frem", password = Password });
        var auth = await accepted.ReadAsync<PortalAuthResponse>();
        auth.Account.Projects.Select(p => p.Id).ShouldBe([s.Project.Id]);
        var cookie = accepted.SetCookieHeaders().Single(h => h.StartsWith(PortalCookie + "=", StringComparison.Ordinal));
        cookie.ShouldContain("path=/api/portal/auth", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);

        var me = await (await MeAsync(auth.AccessToken)).ReadAsync<PortalAccountResponse>();
        me.Projects.Select(p => p.Id).ShouldBe([s.Project.Id]);
        me.Projects.ShouldNotContain(p => p.Id == other.Id);

        // The link works once.
        await (await Client.PostJsonAsync("/api/portal/invitations/accept", new { token, firstName = "Rita", lastName = "Frem", password = Password }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationUsed);

        var after = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.SuperAdmin.AccessToken)).ReadAsync<PortalClientsResponse>();
        after.Clients.Single().Status.ShouldBe(PortalClientStatus.Active);
        after.Clients.Single().Name.ShouldBe("Rita Frem");

        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind IN ('portal_client_invited', 'portal_invitation_accepted')",
            ("@p", s.Project.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task Client_and_staff_tokens_are_never_accepted_by_the_other_side()
    {
        var (client, clientCookie, s) = await ActiveClientAsync();

        foreach (var url in new[]
        {
            "/api/auth/me", "/api/projects", $"/api/projects/{s.Project.Id}", "/api/messages/badge", "/api/notifications/unread-count",
            "/api/presence", "/api/search?q=a", "/api/teams", "/api/company/settings", "/api/files/1", $"/api/projects/{s.Project.Id}/portal/clients",
        })
        {
            ((int)(await Client.GetAsync(url, client.AccessToken)).StatusCode).ShouldBe(401, url);
        }

        await (await MeAsync(s.SuperAdmin.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);

        // Each refresh cookie only works on its own side.
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: clientCookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        (await PortalRefreshAsync(clientCookie)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_link_expires_after_24_hours_and_resending_replaces_it()
    {
        var s = await DesignProjectAsync();
        var invitation = (await InvitedAsync(s)).Clients.Single();
        (invitation.InvitationExpiresAt!.Value - invitation.InvitedAt!.Value).ShouldBe(TimeSpan.FromHours(24));
        var first = api.Emails.InvitationTokenFor(s.ContactEmail);

        await _steps.ScalarAsync<int>(
            "UPDATE portal_invitations SET expires_at = UTC_TIMESTAMP(3) - INTERVAL 1 MINUTE WHERE id = @id; SELECT 1", ("@id", invitation.InvitationId!.Value));
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = first })).ShouldFailWithAsync(422, ErrorCodes.PortalInvitationExpired);
        var listed = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.SuperAdmin.AccessToken)).ReadAsync<PortalClientsResponse>();
        listed.Clients.Single().Status.ShouldBe(PortalClientStatus.InvitationExpired);

        var resent = await (await Client.PostEmptyAsync(
            $"/api/projects/{s.Project.Id}/portal/invitations/{invitation.InvitationId}/resend", s.SuperAdmin.AccessToken)).ReadAsync<PortalClientsResponse>();
        resent.Clients.Single().Status.ShouldBe(PortalClientStatus.Invited);
        var second = api.Emails.InvitationTokenFor(s.ContactEmail);
        second.ShouldNotBe(first);

        await AcceptAsync(second);
    }

    [Fact]
    public async Task A_resent_or_cancelled_link_stops_working_and_unknown_links_are_refused()
    {
        var s = await DesignProjectAsync();
        var invitation = (await InvitedAsync(s)).Clients.Single();
        var first = api.Emails.InvitationTokenFor(s.ContactEmail);

        await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/invitations/{invitation.InvitationId}/resend", s.SuperAdmin.AccessToken);
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = first })).ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);

        var second = api.Emails.InvitationTokenFor(s.ContactEmail);
        var latest = (await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalClientsResponse>()).Clients.Single();
        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/invitations/{latest.InvitationId}/revoke", s.SuperAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/portal/invitations/accept", new { token = second, firstName = "Rita", lastName = "Frem", password = Password }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);

        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = "not-a-real-token" }))
            .ShouldFailWithAsync(404, ErrorCodes.PortalInvitationInvalid);
    }

    [Fact]
    public async Task Only_the_clients_contacts_on_its_Design_client_projects_can_be_invited()
    {
        var s = await DesignProjectAsync();

        await (await InviteAsync(s, AuthTestSteps.NewEmail("stranger"))).ShouldFailWithAsync(400, ErrorCodes.PortalEmailNotClientContact);

        var internalProject = await ProjectForAsync(s.SuperAdmin, null, DomainCodes.Design);
        await (await InviteAsync(s, projectId: internalProject.Id)).ShouldFailWithAsync(422, ErrorCodes.PortalProjectNotEligible);

        // Execution has no client portal at all.
        var siteClient = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Site client",
            label = "Client",
            domainCode = DomainCodes.Execution,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("site") } },
        }, s.SuperAdmin.AccessToken)).ReadAsync<IdOnly>();
        var site = await ProjectForAsync(s.SuperAdmin, siteClient.Id, DomainCodes.Execution);
        await (await InviteAsync(s, projectId: site.Id)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{site.Id}/portal/clients", s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        await (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/archive", s.SuperAdmin.AccessToken)).Content.ReadAsStringAsync();
        await (await InviteAsync(s)).ShouldFailWithAsync(422, ErrorCodes.PortalProjectNotEligible);
    }

    [Fact]
    public async Task Only_people_who_manage_client_access_in_Design_can_invite()
    {
        var s = await DesignProjectAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(s.SuperAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(s.SuperAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(s.SuperAdmin, RoleCodes.Employee, DomainCodes.Design);

        designAdmin.User.Permissions.ShouldContain(PermissionCodes.ClientAccessManage);
        executionAdmin.User.Permissions.ShouldNotContain(PermissionCodes.ClientAccessManage);

        await (await InviteAsync(s, as_: employee)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await InviteAsync(s, as_: executionAdmin)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await InviteAsync(s, as_: designAdmin)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task An_existing_account_is_given_another_project_without_a_second_account_or_password()
    {
        var (_, _, s) = await ActiveClientAsync();
        var second = await ProjectForAsync(s.SuperAdmin, s.ClientId, DomainCodes.Design);
        var emailsBefore = api.Emails.CountTo(s.ContactEmail);

        var invited = await (await InviteAsync(s, projectId: second.Id)).ReadAsync<PortalClientsResponse>();
        invited.Clients.Single().Status.ShouldBe(PortalClientStatus.Active);
        api.Emails.CountTo(s.ContactEmail).ShouldBe(emailsBefore + 1);
        api.Emails.LastTo(s.ContactEmail).TextBody.ShouldContain(second.Title);

        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_accounts WHERE email = @e", ("@e", s.ContactEmail))).ShouldBe(1);
        var signedIn = await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password }))
            .ReadAsync<PortalAuthResponse>();
        signedIn.Account.Projects.Select(p => p.Id).ShouldBe([s.Project.Id, second.Id], ignoreOrder: true);

        await (await InviteAsync(s, projectId: second.Id)).ShouldFailWithAsync(409, ErrorCodes.PortalAlreadyInvited);
    }

    [Fact]
    public async Task Ending_access_hides_the_project_and_signs_the_client_out_when_it_was_their_last()
    {
        var (client, cookie, s) = await ActiveClientAsync();
        var row = (await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalClientsResponse>()).Clients.Single();

        await Task.Delay(1100); // the session cut-off keeps tokens of the same second
        var ended = await (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/access/{row.AccessId}/revoke", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalClientsResponse>();
        ended.Clients.Single().Status.ShouldBe(PortalClientStatus.AccessEnded);

        await (await MeAsync(client.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);

        // Signing in again works, with no project to open.
        var again = await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password }))
            .ReadAsync<PortalAuthResponse>();
        again.Account.Projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Moving_the_project_to_another_client_ends_portal_access()
    {
        var (client, _, s) = await ActiveClientAsync();
        var newClient = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "New client",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("new") } },
        }, s.SuperAdmin.AccessToken)).ReadAsync<IdOnly>();

        await Task.Delay(1100); // the session cut-off keeps tokens of the same second
        (await Client.PutJsonAsync($"/api/projects/{s.Project.Id}/billing", new { billingType = "client", clientId = newClient.Id }, s.SuperAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_project_access WHERE project_id = @p AND revoked_at IS NULL", ("@p", s.Project.Id))).ShouldBe(0);
        await (await MeAsync(client.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Archiving_the_client_ends_its_contacts_access_and_invitations_and_restoring_gives_nothing_back()
    {
        var (client, cookie, s) = await ActiveClientAsync();
        var second = await ProjectForAsync(s.SuperAdmin, s.ClientId, DomainCodes.Design);
        (await InviteAsync(s, projectId: second.Id)).IsSuccessStatusCode.ShouldBeTrue();
        var colleague = AuthTestSteps.NewEmail("colleague");
        (await Client.PutJsonAsync($"/api/clients/{s.ClientId}", new
        {
            name = "Rita Frem",
            label = "Client",
            contacts = new[] { new { kind = "email", value = s.ContactEmail, label = "Office" }, new { kind = "email", value = colleague, label = "Home" } },
        }, s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await InviteAsync(s, colleague)).IsSuccessStatusCode.ShouldBeTrue();
        var colleagueLink = api.Emails.InvitationTokenFor(colleague);

        await Task.Delay(1100); // the session cut-off keeps tokens of the same second
        (await Client.PostEmptyAsync($"/api/clients/{s.ClientId}/archive", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // Both projects end, so the client is signed out; the colleague's link is cancelled.
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_project_access WHERE project_id IN (@p, @q) AND revoked_at IS NULL",
            ("@p", s.Project.Id), ("@q", second.Id))).ShouldBe(0);
        await (await MeAsync(client.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = colleagueLink }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);

        (await Client.PostEmptyAsync($"/api/clients/{s.ClientId}/restore", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var again = await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password }))
            .ReadAsync<PortalAuthResponse>();
        again.Account.Projects.ShouldBeEmpty();
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = colleagueLink }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);
        var listed = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalClientsResponse>();
        listed.Clients.Single(c => c.Email == s.ContactEmail).Status.ShouldBe(PortalClientStatus.AccessEnded);
        listed.Clients.Single(c => c.Email == colleague).Status.ShouldBe(PortalClientStatus.NotInvited);
    }

    [Fact]
    public async Task Taking_an_email_off_the_client_ends_that_contacts_access_and_invitations_only()
    {
        var (client, cookie, s) = await ActiveClientAsync();
        var colleague = AuthTestSteps.NewEmail("colleague");
        Task<HttpResponseMessage> ContactsAsync(params string[] emails) => Client.PutJsonAsync($"/api/clients/{s.ClientId}", new
        {
            name = "Rita Frem",
            label = "Client",
            contacts = emails.Select(e => new { kind = "email", value = e, label = "Office" }).ToArray(),
        }, s.SuperAdmin.AccessToken);
        (await ContactsAsync(s.ContactEmail, colleague)).IsSuccessStatusCode.ShouldBeTrue();
        (await InviteAsync(s, colleague)).IsSuccessStatusCode.ShouldBeTrue();
        var colleagueLink = api.Emails.InvitationTokenFor(colleague);

        await Task.Delay(1100); // the session cut-off keeps tokens of the same second
        // the first contact left the client's company: their address is taken off the client
        (await ContactsAsync(colleague)).IsSuccessStatusCode.ShouldBeTrue();

        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_project_access a JOIN portal_accounts p ON p.id = a.account_id " +
            "WHERE a.project_id = @p AND a.revoked_at IS NULL AND p.email = @e",
            ("@p", s.Project.Id), ("@e", s.ContactEmail))).ShouldBe(0);
        await (await MeAsync(client.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        // the colleague is still a contact: their link still works
        (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = colleagueLink })).IsSuccessStatusCode.ShouldBeTrue();

        // a changed address is taken off too
        (await ContactsAsync(AuthTestSteps.NewEmail("colleague-new"))).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = colleagueLink }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);
    }

    [Fact]
    public async Task A_link_is_refused_once_its_project_is_archived_and_accepting_another_link_cancels_it()
    {
        var s = await DesignProjectAsync();
        var second = await ProjectForAsync(s.SuperAdmin, s.ClientId, DomainCodes.Design);
        await InvitedAsync(s);
        var first = api.Emails.InvitationTokenFor(s.ContactEmail);
        (await InviteAsync(s, projectId: second.Id)).IsSuccessStatusCode.ShouldBeTrue();
        var toSecond = api.Emails.InvitationTokenFor(s.ContactEmail);

        (await Client.PostEmptyAsync($"/api/projects/{second.Id}/archive", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/portal/invitations/validate", new { token = toSecond }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);
        await (await Client.PostJsonAsync("/api/portal/invitations/accept", new { token = toSecond, firstName = "Rita", lastName = "Frem", password = Password }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);

        // The other link still works and opens only its own project; the archived project's invitation is cancelled.
        var (auth, _) = await AcceptAsync(first);
        auth.Account.Projects.Select(p => p.Id).ShouldBe([s.Project.Id]);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_invitations WHERE project_id = @p AND revoked_at IS NOT NULL", ("@p", second.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_link_is_refused_once_the_address_is_no_longer_one_of_the_clients_contacts()
    {
        var s = await DesignProjectAsync();
        await InvitedAsync(s);
        var token = api.Emails.InvitationTokenFor(s.ContactEmail);

        (await Client.PutJsonAsync($"/api/clients/{s.ClientId}", new
        {
            name = "Rita Frem",
            label = "Client",
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("replacement"), label = "Office" } },
        }, s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PostJsonAsync("/api/portal/invitations/accept", new { token, firstName = "Rita", lastName = "Frem", password = Password }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_project_access WHERE project_id = @p", ("@p", s.Project.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task A_link_is_refused_while_its_client_is_archived()
    {
        var s = await DesignProjectAsync();
        await InvitedAsync(s);
        var token = api.Emails.InvitationTokenFor(s.ContactEmail);

        // Archived behind the API's back, so the invitation is still pending and only the check on accepting stops it.
        await _steps.ScalarAsync<int>("UPDATE clients SET archived_at = UTC_TIMESTAMP(3) WHERE id = @id; SELECT 1", ("@id", s.ClientId));

        await (await Client.PostJsonAsync("/api/portal/invitations/accept", new { token, firstName = "Rita", lastName = "Frem", password = Password }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationRevoked);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_project_access WHERE project_id = @p", ("@p", s.Project.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Two_pending_invitations_to_one_project_are_accepted_as_one_access()
    {
        var s = await DesignProjectAsync();
        var invitation = (await InvitedAsync(s)).Clients.Single();
        var token = api.Emails.InvitationTokenFor(s.ContactEmail);

        // What two invitations sent at the same moment could leave: a second pending invitation to the same project.
        await _steps.ScalarAsync<int>(
            "INSERT INTO portal_invitations (account_id, project_id, token_hash, sent_at, expires_at, invited_by_id) " +
            "SELECT account_id, project_id, @hash, sent_at, expires_at, invited_by_id FROM portal_invitations WHERE id = @id; SELECT 1",
            ("@hash", Guid.NewGuid().ToString("N")), ("@id", invitation.InvitationId!.Value));

        var (auth, _) = await AcceptAsync(token);
        auth.Account.Projects.Select(p => p.Id).ShouldBe([s.Project.Id]);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_project_access WHERE project_id = @p", ("@p", s.Project.Id))).ShouldBe(1);

        // No link of the account is left waiting.
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_invitations WHERE project_id = @p AND accepted_at IS NULL AND revoked_at IS NULL", ("@p", s.Project.Id)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task An_accepted_invitation_cannot_be_cancelled_and_access_stays()
    {
        var (client, _, s) = await ActiveClientAsync();
        var invitationId = await _steps.ScalarAsync<long>("SELECT id FROM portal_invitations WHERE project_id = @p", ("@p", s.Project.Id));

        await (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/invitations/{invitationId}/revoke", s.SuperAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalInvitationUsed);

        (await MeAsync(client.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_invitations WHERE id = @id AND accepted_at IS NOT NULL AND revoked_at IS NULL", ("@id", invitationId))).ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_a_reused_one_ends_every_session()
    {
        var (_, cookie, _) = await ActiveClientAsync();

        var refreshed = await PortalRefreshAsync(cookie);
        refreshed.IsSuccessStatusCode.ShouldBeTrue();
        var next = PortalCookieOf(refreshed)!;
        next.ShouldNotBe(cookie);

        // the old cookie presented again after the reuse grace
        await _steps.ScalarAsync<int>(
            "UPDATE portal_refresh_tokens SET revoked_at = UTC_TIMESTAMP(3) - INTERVAL 5 MINUTE WHERE replaced_by_id IS NOT NULL; SELECT 1");
        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await PortalRefreshAsync(next)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Sign_in_is_generic_and_pauses_after_repeated_wrong_passwords()
    {
        var (_, _, s) = await ActiveClientAsync();
        var unknown = await Client.PostJsonAsync("/api/portal/auth/login", new { email = AuthTestSteps.NewEmail("nobody"), password = Password });
        var unknownError = await unknown.ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);

        for (var i = 0; i < 10; i++)
        {
            await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = "wrong-password-123" }))
                .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        }

        // Paused: even the right password gets the same answer as an unknown email.
        var paused = await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        paused.Message.ShouldBe(unknownError.Message);

        await _steps.ScalarAsync<int>(
            "UPDATE portal_accounts SET locked_until = UTC_TIMESTAMP(3) - INTERVAL 1 MINUTE WHERE email = @e; SELECT 1", ("@e", s.ContactEmail));
        (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password })).IsSuccessStatusCode.ShouldBeTrue();

        // A staff sign-in with the client's credentials finds no one.
        await (await Client.PostJsonAsync("/api/auth/login", new { email = s.ContactEmail, password = Password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Wrong_passwords_sent_at_once_all_count_towards_the_pause()
    {
        var (_, _, s) = await ActiveClientAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = "wrong-password-123" })));
        attempts.ShouldAllBe(r => (int)r.StatusCode == 401);

        await (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = Password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task A_forgotten_password_is_reset_by_email_and_signs_the_client_out()
    {
        var (client, cookie, s) = await ActiveClientAsync();

        (await Client.PostJsonAsync("/api/portal/auth/forgot-password", new { email = s.ContactEmail.ToUpperInvariant() })).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        await WaitForAsync(() => api.Emails.LastTo(s.ContactEmail).TextBody.Contains("/portal/reset-password"));
        var token = api.Emails.ResetTokenFor(s.ContactEmail);

        await Task.Delay(1100);
        (await Client.PostJsonAsync("/api/portal/auth/reset-password", new { token, password = "a-brand-new-passphrase" })).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.PostJsonAsync("/api/portal/auth/reset-password", new { token, password = "another-new-passphrase" }))
            .ShouldFailWithAsync(404, ErrorCodes.PasswordResetInvalid);

        await (await MeAsync(client.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        (await Client.PostJsonAsync("/api/portal/auth/login", new { email = s.ContactEmail, password = "a-brand-new-passphrase" }))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var (_, cookie, _) = await ActiveClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/portal/auth/logout");
        request.Headers.Add("Cookie", $"{PortalCookie}={cookie}");
        var response = await Client.SendAsync(request);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        response.SetCookieHeaders().ShouldContain(h => h.StartsWith(PortalCookie + "=;", StringComparison.Ordinal) || h.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        await (await PortalRefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    /// <summary>The reset email is sent without the request waiting for it.</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !Safe(condition); i++)
        {
            await Task.Delay(100);
        }

        Safe(condition).ShouldBeTrue();
    }

    private static bool Safe(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
