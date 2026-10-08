using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Pushes through a fake provider: what is sent, to which phones, and what is checked again at send time.</summary>
public class PushTests(PushApiFactory api) : IClassFixture<PushApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private static bool QuietNow()
        => QuietHours.Contains(QuietHours.DefaultFromMinute, QuietHours.DefaultToMinute, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut)));

    /// <summary>Signs in on a phone and registers it; returns the session and the push token.</summary>
    private async Task<(MobileAuthResponse Phone, string Token)> PhoneAsync(string email, string password)
    {
        var phone = await (await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password })).ReadAsync<MobileAuthResponse>();
        var token = $"ExponentPushToken[{Guid.NewGuid():N}]";
        var registered = await Client.PutJsonAsync("/api/me/devices", new { pushToken = token, platform = "android" }, phone.AccessToken);
        registered.IsSuccessStatusCode.ShouldBeTrue(await registered.Content.ReadAsStringAsync());
        return (phone, token);
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

    [Fact]
    public async Task A_notification_reaches_the_person_s_phone_with_safe_text_and_only_ids_in_its_data()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (email, password, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, token) = await PhoneAsync(email, password);

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var push = await api.Push.WaitForAsync(token, m => m.Data.Kind == NotificationKinds.ProjectCreated);
        push.Title.ShouldBe("New project");
        push.Body.ShouldBe($"Test admin added {project.Title}.");
        push.Data.ForUserId.ShouldBe(employee.User.Id);
        push.Data.NotificationId.ShouldNotBeNull();
        push.Data.Target.ShouldBe(new PushTarget { Type = PushTarget.Project, Id = project.Id, Domain = DomainCodes.Design });
        push.Badge.ShouldBeGreaterThanOrEqualTo(1);
        push.ChannelId.ShouldBe(QuietNow() ? PushCategories.QuietChannel : "projects");
        push.Sound.ShouldBe(!QuietNow());
    }

    [Fact]
    public async Task A_direct_message_reaches_the_other_person_s_phone_with_the_sender_and_a_preview()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (phone, token) = await PhoneAsync(email, password);

        var sent = await Client.PostJsonAsync($"/api/messages/direct/{phone.User.Id}", new { body = "Can you send the drawings today?" }, superAdmin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(201, await sent.Content.ReadAsStringAsync());

        var push = await api.Push.WaitForAsync(token, m => m.Data.Kind == PushKinds.Message);
        push.Title.ShouldBe("Rana Haddad");
        push.Body.ShouldBe("Can you send the drawings today?");
        push.Data.NotificationId.ShouldBeNull();
        push.Data.Target.ShouldBe(PushTarget.Chat(null, superAdmin.User.Id));
        push.Badge.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Who_may_read_a_notification_is_checked_again_when_it_is_sent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, token) = await PhoneAsync(email, password);

        await DeliverAsync(new NotificationPushJob(admin.User.Id, await NotifyAsync(admin.User.Id, NotificationKinds.InvoiceIssued), false));
        api.Push.SentTo(token).ShouldContain(m => m.Data.Kind == NotificationKinds.InvoiceIssued && m.Body == "Billing update. Open ID InDetails to see it.");

        var revoked = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "No longer handles money" } } },
            superAdmin.AccessToken);
        revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync());
        var before = api.Push.SentTo(token).Count(m => m.Data.Kind == NotificationKinds.InvoiceIssued);

        await DeliverAsync(new NotificationPushJob(admin.User.Id, await NotifyAsync(admin.User.Id, NotificationKinds.InvoiceIssued), false));
        api.Push.SentTo(token).Count(m => m.Data.Kind == NotificationKinds.InvoiceIssued).ShouldBe(before);
    }

    [Fact]
    public async Task Phones_whose_session_ended_get_nothing_and_are_cleaned_up()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, token) = await PhoneAsync(email, password);

        // a password change on the website ends the phone's session; its device row stays until the cleanup
        (await Client.PostJsonAsync("/api/auth/change-password",
            new { currentPassword = password, newPassword = "a-brand-new-passphrase" }, person.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await DeliverAsync(new NotificationPushJob(person.User.Id, await NotifyAsync(person.User.Id, NotificationKinds.PermissionsChanged), false));

        api.Push.SentTo(token).ShouldBeEmpty();
        await using var scope = api.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IDeviceService>().DeleteDeadAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM user_devices WHERE push_token = @t", ("@t", token))).ShouldBe(0);
    }

    [Fact]
    public async Task A_device_Expo_no_longer_knows_is_deleted()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, token) = await PhoneAsync(email, password);
        api.Push.Unregistered[token] = true;

        await DeliverAsync(new NotificationPushJob(person.User.Id, await NotifyAsync(person.User.Id, NotificationKinds.PermissionsChanged), false));

        api.Push.SentTo(token).ShouldHaveSingleItem();
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM user_devices WHERE push_token = @t", ("@t", token))).ShouldBe(0);
    }

    [Fact]
    public async Task A_digest_pushes_in_full_once_an_hour_and_otherwise_only_updates_the_badge()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, token) = await PhoneAsync(email, password);
        var digest = await NotifyAsync(person.User.Id, NotificationKinds.RequirementsAdded);

        await DeliverAsync(new NotificationPushJob(person.User.Id, digest, true));
        await DeliverAsync(new NotificationPushJob(person.User.Id, digest, true));

        var pushes = api.Push.SentTo(token);
        pushes.Count.ShouldBe(2);
        pushes[0].Body.ShouldNotBeNull();
        pushes[1].ShouldSatisfyAllConditions(
            m => m.Title.ShouldBeNull(),
            m => m.Body.ShouldBeNull(),
            m => m.Sound.ShouldBeFalse(),
            m => m.Badge.ShouldBeGreaterThanOrEqualTo(1));
    }
}
