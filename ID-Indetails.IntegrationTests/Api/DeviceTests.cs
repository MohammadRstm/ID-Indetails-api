using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Registering phones for pushes, tied to the phone app's session.</summary>
public class DeviceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static string NewToken() => $"ExponentPushToken[{Guid.NewGuid():N}]";

    private async Task<MobileAuthResponse> PhoneLoginAsync(string email, string password)
    {
        var response = await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MobileAuthResponse>();
    }

    private async Task<DeviceResponse> RegisterAsync(string accessToken, string pushToken, string platform = "android")
    {
        var response = await Client.PutJsonAsync("/api/me/devices", new { pushToken, platform, appVersion = "1.0.0" }, accessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<DeviceResponse>();
    }

    private async Task<IReadOnlyList<DeviceResponse>> ListAsync(string accessToken)
        => await (await Client.GetAsync("/api/me/devices", accessToken)).ReadAsync<List<DeviceResponse>>();

    [Fact]
    public async Task A_phone_registers_once_per_session_and_a_new_token_replaces_its_old_one()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);

        var first = await RegisterAsync(phone.AccessToken, NewToken(), "ios");
        first.Platform.ShouldBe(DevicePlatform.Ios);
        first.AppVersion.ShouldBe("1.0.0");
        first.Current.ShouldBeTrue();

        var second = await RegisterAsync(phone.AccessToken, NewToken(), "ios");
        var listed = await ListAsync(phone.AccessToken);
        listed.Select(d => d.Id).ShouldBe([second.Id]);
        listed[0].Current.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_the_phone_app_registers_and_the_request_is_checked()
    {
        var (_, _, web) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PutJsonAsync("/api/me/devices", new { pushToken = NewToken(), platform = "android" }, web.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync("/api/me/devices", new { pushToken = "not-a-token", platform = "android" }, web.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        ((int)(await Client.DeleteAsync("/api/me/devices/current", web.AccessToken)).StatusCode).ShouldBe(204);
    }

    [Fact]
    public async Task A_token_registered_by_someone_else_moves_to_whoever_signs_in_on_that_phone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (rana, ranaPassword, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (sami, samiPassword, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var shared = NewToken();

        var ranaPhone = await PhoneLoginAsync(rana, ranaPassword);
        await RegisterAsync(ranaPhone.AccessToken, shared);
        var samiPhone = await PhoneLoginAsync(sami, samiPassword);
        var moved = await RegisterAsync(samiPhone.AccessToken, shared);

        (await ListAsync(ranaPhone.AccessToken)).ShouldBeEmpty();
        (await ListAsync(samiPhone.AccessToken)).Select(d => d.Id).ShouldBe([moved.Id]);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM user_devices WHERE push_token = @t", ("@t", shared))).ShouldBe(1);
    }

    [Fact]
    public async Task Turning_pushes_off_and_signing_out_remove_the_device()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);

        await RegisterAsync(phone.AccessToken, NewToken());
        ((int)(await Client.DeleteAsync("/api/me/devices/current", phone.AccessToken)).StatusCode).ShouldBe(204);
        (await ListAsync(phone.AccessToken)).ShouldBeEmpty();

        var token = NewToken();
        await RegisterAsync(phone.AccessToken, token);
        ((int)(await Client.PostJsonAsync("/api/mobile/auth/logout", new { refreshToken = phone.RefreshToken })).StatusCode).ShouldBe(204);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM user_devices WHERE push_token = @t", ("@t", token))).ShouldBe(0);
    }

    [Fact]
    public async Task Removing_another_of_your_phones_signs_it_out_but_nobody_removes_someone_else_s()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var phone = await PhoneLoginAsync(email, password);
        var tablet = await PhoneLoginAsync(email, password);
        await RegisterAsync(phone.AccessToken, NewToken());
        var tabletDevice = await RegisterAsync(tablet.AccessToken, NewToken());

        var listed = await ListAsync(phone.AccessToken);
        listed.Select(d => d.Id).ShouldBe([tabletDevice.Id, listed.Single(d => d.Current).Id]);
        await (await Client.DeleteAsync($"/api/me/devices/{tabletDevice.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        ((int)(await Client.DeleteAsync($"/api/me/devices/{tabletDevice.Id}", phone.AccessToken)).StatusCode).ShouldBe(204);

        await (await Client.PostJsonAsync("/api/mobile/auth/refresh", new { refreshToken = tablet.RefreshToken }))
            .ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        (await ListAsync(phone.AccessToken)).ShouldHaveSingleItem().Current.ShouldBeTrue();
    }

    [Fact]
    public async Task Phones_whose_session_ended_are_not_listed()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var other = await PhoneLoginAsync(email, password);
        await RegisterAsync(phone.AccessToken, NewToken());
        await RegisterAsync(other.AccessToken, NewToken());

        // changing the password on this phone ends the other one's session; its access token still works a few minutes
        var changed = await (await Client.PostJsonAsync("/api/mobile/auth/change-password",
            new { currentPassword = password, newPassword = "another-good-passphrase" }, phone.AccessToken)).ReadAsync<MobileAuthResponse>();

        (await ListAsync(changed.AccessToken)).ShouldHaveSingleItem().Current.ShouldBeTrue();
    }
}
