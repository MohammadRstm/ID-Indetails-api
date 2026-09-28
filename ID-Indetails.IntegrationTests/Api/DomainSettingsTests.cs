using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class DomainSettingsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Defaults_apply_until_the_admin_saves_and_everyone_of_the_domain_can_read_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var defaults = await (await Client.GetAsync("/api/domain-settings/design", employee.AccessToken)).ReadAsync<DomainSettingsResponse>();
        defaults.EditWindow.ShouldBe(EditWindow.OneDay);
        defaults.RequireDescription.ShouldBeTrue();
        defaults.HoldLogging.ShouldBeFalse();
        defaults.UpdatedAt.ShouldBeNull();

        var saved = await (await Client.PutJsonAsync("/api/domain-settings/design", new
        {
            editWindow = "seven_days",
            allowOverlap = true,
            requireDescription = false,
            timerEnabled = true,
            notifyEntry = "digest",
            holdLogging = true,
        }, admin.AccessToken)).ReadAsync<DomainSettingsResponse>();
        saved.EditWindow.ShouldBe(EditWindow.SevenDays);
        saved.NotifyEntry.ShouldBe(EntryNotificationMode.Digest);
        saved.UpdatedAt.ShouldNotBeNull();

        var read = await (await Client.GetAsync("/api/domain-settings/design", employee.AccessToken)).ReadAsync<DomainSettingsResponse>();
        read.AllowOverlap.ShouldBeTrue();
        read.HoldLogging.ShouldBeTrue();
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM activity_events WHERE kind = 'domain_settings_updated' AND domain_code = 'design'"))
            .ShouldBeGreaterThanOrEqualTo(1);

        await (await Client.PutJsonAsync("/api/domain-settings/design", new { editWindow = "forever" }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Employees_cannot_change_settings_and_other_domains_read_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);

        await (await Client.PutJsonAsync("/api/domain-settings/design", new { editWindow = "none" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/domain-settings/design", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync("/api/domain-settings/design", new { editWindow = "none" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/domain-settings/company", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await Client.PutJsonAsync("/api/domain-settings/execution", new { editWindow = "unlimited" }, executionAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }
}
