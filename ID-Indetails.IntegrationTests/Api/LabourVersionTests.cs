using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Labour;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Labour weeks submitted twice, or after someone else changed them (the phone app's offline weeks).</summary>
public class LabourVersionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static readonly DateOnly ThisWeek = LabourRules.WeekStartOf(
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"))));

    private string WeekUrl(long siteId) => $"/api/projects/{siteId}/labour-weeks/{ThisWeek:yyyy-MM-dd}";

    private static object Week(int workers, string? note = null, string? expectedVersion = null)
        => new { days = new[] { new { date = ThisWeek, workers, dailyPay = 25m } }, note, expectedVersion };

    private async Task<LabourWeekResponse> SubmitAsync(long siteId, string token, object body)
    {
        var response = await Client.PutJsonAsync(WeekUrl(siteId), body, token);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LabourWeekResponse>();
    }

    private Task<int> CountAsync(string sql, long siteId) => _steps.ScalarAsync<int>(sql, ("@p", siteId));

    [Fact]
    public async Task A_week_has_a_version_and_sending_the_same_week_again_changes_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [worker.User.Id]);

        (await (await Client.GetAsync(WeekUrl(site.Id), worker.AccessToken)).ReadAsync<LabourWeekResponse>()).Version.ShouldBe("0");

        var submitted = await SubmitAsync(site.Id, worker.AccessToken, Week(4, "Slab", expectedVersion: "0"));
        submitted.Version.ShouldNotBe("0");
        var notified = await CountAsync("SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'labour_week_submitted'", site.Id);

        // the phone's retry after a lost answer, still with the version it loaded, and the website's identical save
        var retried = await SubmitAsync(site.Id, worker.AccessToken, Week(4, " Slab ", expectedVersion: "0"));
        var resaved = await SubmitAsync(site.Id, worker.AccessToken, Week(4, "Slab"));

        retried.Version.ShouldBe(submitted.Version);
        resaved.Version.ShouldBe(submitted.Version);
        resaved.UpdatedAt.ShouldBeNull();
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind IN ('labour_week_submitted', 'labour_week_updated')", site.Id))
            .ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'labour_week_submitted'", site.Id)).ShouldBe(notified);
    }

    [Fact]
    public async Task A_week_changed_by_someone_else_since_it_was_loaded_is_a_conflict_unless_it_says_the_same()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [worker.User.Id]);
        var loaded = await SubmitAsync(site.Id, worker.AccessToken, Week(4));

        // a manager changes it meanwhile
        var changed = await SubmitAsync(site.Id, superAdmin.AccessToken, Week(6));
        changed.Version.ShouldNotBe(loaded.Version);

        await (await Client.PutJsonAsync(WeekUrl(site.Id), Week(5, expectedVersion: loaded.Version), worker.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.LabourWeekChanged);
        (await SubmitAsync(site.Id, worker.AccessToken, Week(6, expectedVersion: loaded.Version))).Version.ShouldBe(changed.Version);

        // "use mine": sent again against the new version, it is saved
        var mine = await SubmitAsync(site.Id, worker.AccessToken, Week(5, expectedVersion: changed.Version));
        mine.Days.ShouldHaveSingleItem().Workers.ShouldBe(5);
        mine.Version.ShouldNotBe(changed.Version);

        // the website sends no version and saves as before
        (await SubmitAsync(site.Id, superAdmin.AccessToken, Week(7))).Days.Single().Workers.ShouldBe(7);
    }
}
