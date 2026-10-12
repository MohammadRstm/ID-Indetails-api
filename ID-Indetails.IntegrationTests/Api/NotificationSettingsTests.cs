using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Each person's own quiet hours and muted push categories.</summary>
public class NotificationSettingsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<NotificationSettingsResponse> SaveAsync(string token, object body)
    {
        var response = await Client.PutJsonAsync("/api/me/notification-settings", body, token);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<NotificationSettingsResponse>();
    }

    private async Task<NotificationSettingsResponse> GetAsync(string token)
        => await (await Client.GetAsync("/api/me/notification-settings", token)).ReadAsync<NotificationSettingsResponse>();

    [Fact]
    public async Task Until_someone_saves_their_own_they_get_22_00_to_07_00_with_nothing_muted()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        (await GetAsync(superAdmin.AccessToken)).ShouldSatisfyAllConditions(
            s => s.QuietFromMinute.ShouldBe(1320),
            s => s.QuietToMinute.ShouldBe(420),
            s => s.MutedCategories.ShouldBeEmpty(),
            s => s.IsDefault.ShouldBeTrue());
    }

    [Fact]
    public async Task Saving_replaces_the_settings_and_each_person_sees_only_their_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var saved = await SaveAsync(employee.AccessToken, new { quietFromMinute = 1380, quietToMinute = 360, mutedCategories = new[] { "money", "people" } });
        saved.ShouldSatisfyAllConditions(
            s => s.QuietFromMinute.ShouldBe(1380),
            s => s.QuietToMinute.ShouldBe(360),
            s => s.MutedCategories.ShouldBe([NotificationCategory.Money, NotificationCategory.People], ignoreOrder: true),
            s => s.IsDefault.ShouldBeFalse());

        var replaced = await SaveAsync(employee.AccessToken, new { quietFromMinute = (int?)null, quietToMinute = (int?)null, mutedCategories = new[] { "people", "work" } });
        replaced.QuietFromMinute.ShouldBeNull();
        replaced.QuietToMinute.ShouldBeNull();
        replaced.MutedCategories.ShouldBe([NotificationCategory.People, NotificationCategory.Work], ignoreOrder: true);
        var readBack = await GetAsync(employee.AccessToken);
        readBack.QuietFromMinute.ShouldBeNull();
        readBack.MutedCategories.ShouldBe(replaced.MutedCategories, ignoreOrder: true);
        readBack.IsDefault.ShouldBeFalse();

        (await GetAsync(superAdmin.AccessToken)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Half_set_or_unknown_settings_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PutJsonAsync("/api/me/notification-settings", new { quietFromMinute = 1320, quietToMinute = (int?)null, mutedCategories = Array.Empty<string>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync("/api/me/notification-settings", new { quietFromMinute = 1320, quietToMinute = 2000, mutedCategories = Array.Empty<string>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync("/api/me/notification-settings", new { quietFromMinute = 1320, quietToMinute = 420, mutedCategories = new[] { "weather" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await GetAsync(superAdmin.AccessToken)).IsDefault.ShouldBeTrue();
    }
}
