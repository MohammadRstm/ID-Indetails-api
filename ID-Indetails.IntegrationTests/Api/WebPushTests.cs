using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Browsers registering for pushes (Web Push), tied to the website's session the refresh cookie names.</summary>
public class WebPushTests(PushApiFactory api) : IClassFixture<PushApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    /// <summary>A real browser's key and secret (RFC 8291's example); the endpoint is new each time.</summary>
    private static object Subscription(string endpoint) => new
    {
        endpoint,
        p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
        auth = "BTBZMqHH6r4Tts7J_aSIgg",
    };

    private static string NewEndpoint() => $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

    /// <summary>Signs in on the website, with no quiet hours so a test doesn't depend on the time of day.</summary>
    private async Task<(AuthResponse Auth, string Cookie)> BrowserAsync(string email, string password)
    {
        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password, installedApp = true });
        var auth = await login.ShouldSignInAsync();
        var saved = await Client.PutJsonAsync("/api/me/notification-settings",
            new { quietFromMinute = (int?)null, quietToMinute = (int?)null, mutedCategories = Array.Empty<string>() }, auth.AccessToken);
        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
        return (auth, login.RefreshCookie()!);
    }

    private async Task RegisterAsync(AuthResponse auth, string cookie, string endpoint)
    {
        var response = await Client.PutJsonAsync("/api/auth/web-push", Subscription(endpoint), auth.AccessToken, cookie);
        ((int)response.StatusCode).ShouldBe(204, await response.Content.ReadAsStringAsync());
    }

    private Task<long> NotifyAsync(long recipientId, string kind)
        => _steps.ScalarAsync<long>(
            "INSERT INTO notifications (recipient_id, kind, count, created_at) VALUES (@r, @k, 1, UTC_TIMESTAMP(3)); SELECT LAST_INSERT_ID();",
            ("@r", recipientId), ("@k", kind));

    private async Task DeliverAsync(params PushJob[] jobs)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPushDelivery>().DeliverAsync(jobs);
    }

    private Task<int> DevicesAsync(string endpoint)
        => _steps.ScalarAsync<int>("SELECT COUNT(*) FROM user_devices WHERE push_token = @t", ("@t", WebPushEndpoints.PushTokenOf(endpoint)));

    [Fact]
    public async Task A_registered_browser_gets_the_person_s_pushes_encrypted_to_its_subscription()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (auth, cookie) = await BrowserAsync(email, password);
        var key = await (await Client.GetAsync("/api/auth/web-push", auth.AccessToken)).ReadAsync<WebPushKeyResponse>();
        key.PublicKey.ShouldNotBeNullOrEmpty();
        var endpoint = NewEndpoint();
        await RegisterAsync(auth, cookie, endpoint);

        var notification = await NotifyAsync(person.User.Id, NotificationKinds.PermissionsChanged);
        await DeliverAsync(new NotificationPushJob(person.User.Id, notification, false));

        var push = api.Push.SentTo(WebPushEndpoints.PushTokenOf(endpoint)).ShouldHaveSingleItem();
        push.WebPush.ShouldNotBeNull().Endpoint.ShouldBe(endpoint);
        push.Title.ShouldBe("Permissions changed");
        push.Data.ForUserId.ShouldBe(person.User.Id);
        push.Data.NotificationId.ShouldBe(notification);
        (await _steps.ScalarAsync<string>(
                "SELECT CONCAT(provider, ':', platform) FROM user_devices WHERE push_token = @t", ("@t", WebPushEndpoints.PushTokenOf(endpoint))))
            .ShouldBe("web_push:web");
    }

    [Fact]
    public async Task Registering_needs_the_caller_s_own_live_website_session()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (auth, cookie) = await BrowserAsync(email, password);
        var (otherEmail, otherPassword, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, otherCookie) = await BrowserAsync(otherEmail, otherPassword);
        var endpoint = NewEndpoint();

        await (await Client.PutJsonAsync("/api/auth/web-push", Subscription(endpoint), auth.AccessToken))
            .ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PutJsonAsync("/api/auth/web-push", Subscription(endpoint), auth.AccessToken, otherCookie))
            .ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PutJsonAsync("/api/auth/web-push", Subscription("https://10.0.0.5/internal"), auth.AccessToken, cookie))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        await Client.PostEmptyAsync("/api/auth/logout", refreshCookie: cookie);
        await (await Client.PutJsonAsync("/api/auth/web-push", Subscription(endpoint), auth.AccessToken, cookie))
            .ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        (await DevicesAsync(endpoint)).ShouldBe(0);
    }

    [Fact]
    public async Task Turning_it_off_or_signing_out_stops_the_browser_s_pushes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (auth, cookie) = await BrowserAsync(email, password);
        var endpoint = NewEndpoint();

        await RegisterAsync(auth, cookie, endpoint);
        ((int)(await Client.DeleteAsync("/api/auth/web-push", auth.AccessToken, cookie)).StatusCode).ShouldBe(204);
        (await DevicesAsync(endpoint)).ShouldBe(0);

        await RegisterAsync(auth, cookie, endpoint);
        (await DevicesAsync(endpoint)).ShouldBe(1);
        await Client.PostEmptyAsync("/api/auth/logout", refreshCookie: cookie);
        (await DevicesAsync(endpoint)).ShouldBe(0);
    }

    [Fact]
    public async Task On_a_shared_computer_the_subscription_moves_to_whoever_registered_it_last()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (nextEmail, nextPassword, next) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var endpoint = NewEndpoint();

        var (auth, cookie) = await BrowserAsync(email, password);
        await RegisterAsync(auth, cookie, endpoint);
        var (nextAuth, nextCookie) = await BrowserAsync(nextEmail, nextPassword);
        await RegisterAsync(nextAuth, nextCookie, endpoint);

        (await DevicesAsync(endpoint)).ShouldBe(1);
        (await _steps.ScalarAsync<long>("SELECT user_id FROM user_devices WHERE push_token = @t", ("@t", WebPushEndpoints.PushTokenOf(endpoint))))
            .ShouldBe(next.User.Id);
    }

    [Fact]
    public async Task A_browser_gets_no_push_that_would_only_update_the_badge()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (auth, cookie) = await BrowserAsync(email, password);
        var endpoint = NewEndpoint();
        await RegisterAsync(auth, cookie, endpoint);
        var digest = await NotifyAsync(person.User.Id, NotificationKinds.RequirementsAdded);

        await DeliverAsync(new NotificationPushJob(person.User.Id, digest, true));
        await DeliverAsync(new NotificationPushJob(person.User.Id, digest, true));

        api.Push.SentTo(WebPushEndpoints.PushTokenOf(endpoint)).ShouldHaveSingleItem().Body.ShouldNotBeNull();
    }
}
