using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.DevSignIn;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The development sign-in: any account that can sign in, without its password, for the sign-in page's account picker.
/// Program turns it on in the Development environment only; here a test host turns it on.
/// </summary>
public class DevSignInTests(DevSignInApiFactory api) : IClassFixture<DevSignInApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Lists_each_kind_of_account_and_signs_in_as_any_of_them_without_a_password()
    {
        var (superAdminEmail, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (designAdminEmail, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (designerEmail, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (siteEmail, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var invited = await InvitedOnlyAsync(superAdmin);
        var client = await PortalAccountAsync(superAdmin);

        var accounts = await (await Client.GetAsync("/api/dev/accounts")).ReadAsync<DevAccountsResponse>();
        GroupOf(accounts, superAdminEmail).ShouldBe("Super admins");
        GroupOf(accounts, designAdminEmail).ShouldBe("Design admins");
        GroupOf(accounts, designerEmail).ShouldBe("Design employees");
        GroupOf(accounts, siteEmail).ShouldBe("Execution employees");
        GroupOf(accounts, client.Email).ShouldBe("Clients (portal)");
        // someone who hasn't set a password yet can't sign in, so isn't offered
        accounts.Groups.SelectMany(g => g.Accounts).ShouldNotContain(a => a.Email == invited.Email);

        // super admins first, then each domain's admins before its employees, Design before Execution, clients last
        var labels = accounts.Groups.Select(g => g.Label).ToList();
        labels[0].ShouldBe("Super admins");
        labels.IndexOf("Design admins").ShouldBeLessThan(labels.IndexOf("Design employees"));
        labels.IndexOf("Design employees").ShouldBeLessThan(labels.IndexOf("Execution employees"));
        labels[^1].ShouldBe("Clients (portal)");

        // a staff member: the session a right password gives, with its refresh cookie
        var signedIn = await Client.PostJsonAsync("/api/dev/sign-in", new { userId = designer.User.Id });
        signedIn.RefreshCookie().ShouldNotBeNullOrWhiteSpace();
        var auth = await signedIn.ShouldSignInAsync();
        auth.User.Email.ShouldBe(designerEmail);
        var me = await (await Client.GetAsync("/api/auth/me", auth.AccessToken)).ReadAsync<CurrentUserResponse>();
        me.Id.ShouldBe(designer.User.Id);

        // a client: a portal session, with the portal's own cookie
        var portal = await Client.PostJsonAsync("/api/dev/portal-sign-in", new { accountId = client.Id });
        portal.IsSuccessStatusCode.ShouldBeTrue(await portal.Content.ReadAsStringAsync());
        portal.SetCookieHeaders().ShouldContain(h => h.StartsWith("idi_portal_refresh=", StringComparison.Ordinal));
        (await portal.ReadAsync<PortalAuthResponse>()).Account.Email.ShouldBe(client.Email);
    }

    [Fact]
    public async Task Refuses_accounts_that_cannot_sign_in()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var invited = await InvitedOnlyAsync(superAdmin);

        await (await Client.PostJsonAsync("/api/dev/sign-in", new { userId = invited.Id }))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync("/api/dev/sign-in", new { userId = long.MaxValue }))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync("/api/dev/sign-in", new { userId = 0 }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/dev/portal-sign-in", new { accountId = long.MaxValue }))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    private static string GroupOf(DevAccountsResponse accounts, string email)
        => accounts.Groups.Single(g => g.Accounts.Any(a => a.Email == email)).Label;

    /// <summary>An Execution employee invited but not signed up yet: no password, so they can't sign in.</summary>
    private async Task<(long Id, string Email)> InvitedOnlyAsync(AuthResponse superAdmin)
    {
        var email = AuthTestSteps.NewEmail("invited");
        var invite = await Client.PostJsonAsync("/api/invitations",
            new { email, role = RoleCodes.Employee, domainCode = DomainCodes.Execution }, superAdmin.AccessToken);
        ((int)invite.StatusCode).ShouldBe(201, await invite.Content.ReadAsStringAsync());
        return (await _steps.ScalarAsync<long>("SELECT id FROM users WHERE email = @e", ("@e", email)), email);
    }

    /// <summary>A client's active portal account, written straight to the database (its password is never used here).</summary>
    private async Task<(long Id, string Email)> PortalAccountAsync(AuthResponse superAdmin)
    {
        var email = AuthTestSteps.NewEmail("client");
        await _steps.ExecuteAsync(
            "INSERT INTO portal_accounts (email, first_name, last_name, password_hash, status, failed_sign_ins, created_at, created_by_id) " +
            "VALUES (@e, 'Rita', 'Frem', 'never-used', 'active', 0, UTC_TIMESTAMP(3), @by)",
            ("@e", email), ("@by", superAdmin.User.Id));
        return (await _steps.ScalarAsync<long>("SELECT id FROM portal_accounts WHERE email = @e", ("@e", email)), email);
    }
}

/// <summary>
/// Outside the Development environment the development sign-in isn't part of the app: its addresses answer exactly as
/// ones that don't exist.
/// </summary>
public class DevSignInAbsentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public Task The_test_host_has_no_development_sign_in()
        => ShouldBeAbsentAsync(api.CreateApiClient());

    [Fact]
    public async Task Production_has_no_development_sign_in()
    {
        var production = new ProductionApiFactory(ProductionApiFactory.Valid);
        await production.InitializeAsync();
        try
        {
            await ShouldBeAbsentAsync(production.CreateApiClient());
        }
        finally
        {
            await production.DisposeAsync();
        }
    }

    private static async Task ShouldBeAbsentAsync(HttpClient client)
    {
        var unknownGet = (await client.GetAsync("/api/no-such-address")).StatusCode;
        var unknownPost = (await client.PostJsonAsync("/api/no-such-address", new { })).StatusCode;
        unknownGet.ShouldNotBe(HttpStatusCode.OK);
        unknownPost.ShouldNotBe(HttpStatusCode.OK);

        (await client.GetAsync("/api/dev/accounts")).StatusCode.ShouldBe(unknownGet);
        (await client.PostJsonAsync("/api/dev/sign-in", new { userId = 1 })).StatusCode.ShouldBe(unknownPost);
        (await client.PostJsonAsync("/api/dev/portal-sign-in", new { accountId = 1 })).StatusCode.ShouldBe(unknownPost);
    }
}

/// <summary>The API with the development sign-in turned on, as Program turns it on in the Development environment.</summary>
public sealed class DevSignInApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddDevSignIn(enabled: true));
    }
}
