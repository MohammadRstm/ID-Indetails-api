using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: the filters and sorts are checked over every project of the domain.</summary>
public class ProjectListTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task<IReadOnlyList<ProjectSummaryResponse>> ListAsync(string query, string token)
    {
        var response = await Client.GetAsync($"/api/projects?pageSize=100&{query}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<PagedResponse<ProjectSummaryResponse>>()).Items;
    }

    private Task SqlAsync(string sql, params (string Name, object Value)[] parameters)
        => _steps.ScalarAsync<int>(sql + "; SELECT 1", parameters);

    [Fact]
    public async Task Design_rows_carry_their_work_for_whoever_sees_everyones_work()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var busy = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var quiet = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await Work.LogAsync(anna.AccessToken, busy.Id, WorkEntryTestSteps.HoursAgo(3), 120);
        await Work.LogAsync(ben.AccessToken, busy.Id, WorkEntryTestSteps.HoursAgo(6), 60);
        await Work.InsertAsync(anna.User.Id, quiet.Id, "2025-01-06 07:00:00", "2025-01-06 08:00:00");

        var rows = await ListAsync("", admin.AccessToken);
        var busyWork = rows.Single(r => r.Id == busy.Id).Work!;
        busyWork.TotalMinutes.ShouldBe(180);
        busyWork.MinutesLast14Days.ShouldBe(180);
        busyWork.ActivityLevel.ShouldBe(ActivityLevel.Quiet);
        busyWork.Weekly.Count.ShouldBe(8);
        busyWork.Weekly.Sum().ShouldBe(180);
        busyWork.LastStartedAt.ShouldNotBeNull();
        busyWork.Contributors.Select(c => c.Id).ShouldBe([anna.User.Id, ben.User.Id]);
        busyWork.ContributorCount.ShouldBe(2);

        var quietWork = rows.Single(r => r.Id == quiet.Id).Work!;
        quietWork.TotalMinutes.ShouldBe(60);
        quietWork.MinutesLast14Days.ShouldBe(0);
        quietWork.ActivityLevel.ShouldBe(ActivityLevel.None);
        quietWork.Weekly.Sum().ShouldBe(0);

        (await ListAsync("domain=execution", superAdmin.AccessToken)).Single(r => r.Id == site.Id).Work.ShouldBeNull();

        // Employees list the projects but can't see everyone's work: the field is left out, not zeroed.
        var json = await (await Client.GetAsync("/api/projects", anna.AccessToken)).Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        items.Select(i => i.TryGetProperty("work", out _)).ShouldNotContain(true);
    }

    [Fact]
    public async Task The_list_filters_by_owner_and_open_period_and_sorts()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var old = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var closed = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var fresh = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var tooling = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling", billingType = "internal", description = "Laptops, licences and plotters" }, admin.AccessToken)).ReadAsync<ProjectResponse>();

        // Started in January 2025: one still open, one completed in February 2025.
        await SqlAsync("UPDATE projects SET start_date = '2025-01-10', progress_pct = 40 WHERE id = @id", ("@id", old.Id));
        await SqlAsync("UPDATE projects SET start_date = '2025-01-10', status = 'completed', completed_at = '2025-02-20 10:00:00', progress_pct = 100 WHERE id = @id",
            ("@id", closed.Id));
        await SqlAsync("UPDATE projects SET progress_pct = 10 WHERE id = @id", ("@id", fresh.Id));
        await Work.InsertAsync(designer.User.Id, old.Id, "2025-03-03 07:00:00", "2025-03-03 09:00:00");
        await Work.InsertAsync(designer.User.Id, closed.Id, "2025-02-10 07:00:00", "2025-02-10 12:00:00");
        await Work.InsertAsync(designer.User.Id, fresh.Id, "2026-02-10 07:00:00", "2026-02-10 08:00:00");

        (await ListAsync($"ownerId={superAdmin.User.Id}", admin.AccessToken)).Select(r => r.Id).ShouldBe([fresh.Id]);

        (await ListAsync("year=2025&quarter=1", admin.AccessToken)).Select(r => r.Id).ShouldBe([old.Id, closed.Id], ignoreOrder: true);
        (await ListAsync("year=2025&quarter=3", admin.AccessToken)).Select(r => r.Id).ShouldBe([old.Id]);
        (await ListAsync("year=2025", admin.AccessToken)).Select(r => r.Id).ShouldBe([old.Id, closed.Id], ignoreOrder: true);
        (await ListAsync("year=2100", admin.AccessToken)).ShouldBeEmpty();

        // The other test's projects share the domain, so the orders are checked over this test's projects.
        const string design = "domain=design";
        long[] ours = [old.Id, closed.Id, fresh.Id, tooling.Id];
        async Task<IEnumerable<long>> SortedAsync(string sort)
            => (await ListAsync($"{design}&sort={sort}", admin.AccessToken)).Select(r => r.Id).Where(ours.Contains);
        (await SortedAsync("progress")).ShouldBe([closed.Id, old.Id, fresh.Id, tooling.Id]);
        (await SortedAsync("hours")).Take(3).ShouldBe([closed.Id, old.Id, fresh.Id]);
        // Tooling has no work, so it counts from when it was added (today), ahead of the older work.
        (await SortedAsync("activity")).ShouldBe([tooling.Id, fresh.Id, old.Id, closed.Id]);
        var byName = await ListAsync($"{design}&sort=name", admin.AccessToken);
        byName.Select(r => r.Title).ShouldBe(byName.Select(r => r.Title).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        (await ListAsync($"{design}&sort=client", admin.AccessToken)).Last().Id.ShouldBe(tooling.Id);
        byName.Single(r => r.Id == tooling.Id).Description.ShouldBe("Laptops, licences and plotters");

        await (await Client.GetAsync("/api/projects?quarter=2", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/projects?year=2025&quarter=5", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/projects?sort=revenue", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
