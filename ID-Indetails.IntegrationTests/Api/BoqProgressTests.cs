using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;
using static ID_Indetails.IntegrationTests.Api.BoqTestSteps;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Progress by value on an Execution site: executed quantities recorded on its agreed BOQ and VO lines by its own staff or
/// a pricing manager, the same day replacing, and the value executed against the agreed value, with prices only for
/// financial:view. Every test follows its own site, since the classes share one database.
/// </summary>
public class BoqProgressTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private BoqTestSteps Boqs => new(_steps);

    private static string Url(long projectId) => $"/api/projects/{projectId}/boqs/progress";

    private static string Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static string DaysAgo(int days) => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).AddDays(-days).ToString("yyyy-MM-dd");

    /// <summary>
    /// A site overseen by <paramref name="staff"/>, with an agreed client BOQ of tiling (100 m² at 10), painting (50 m² at
    /// 20) and a permit (500), and an agreed VO adding 10 m of skirting at 10: 2,100 of agreed work.
    /// </summary>
    private async Task<(ProjectResponse Site, BoqDetailResponse Client, BoqDetailResponse Vo)> SiteAsync(AuthResponse superAdmin, params long[] staff)
    {
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: staff, projectFee: 0m);
        var client = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id,
        [
            Line("Floor tiling", 100m, 10m, code: "1.1", unit: "m2"),
            Line("Wall painting", 50m, 20m, code: "1.2", unit: "m2"),
            Line("Building permit", 1m, 500m, unit: "lot", nature: "permit"),
        ]);
        client = await Boqs.AgreeAsync(superAdmin.AccessToken, client.Id);
        var vo = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "Skirting asked for");
        await Boqs.SaveAsync(superAdmin.AccessToken, vo.Id, [Line("Skirting", 10m, 10m, unit: "m")], "Variation order", reason: "Skirting asked for");
        vo = await Boqs.AgreeAsync(superAdmin.AccessToken, vo.Id);
        return (site, client, vo);
    }

    private static long LineId(BoqDetailResponse boq, string description) => boq.Lines.Single(l => l.Description == description).Id;

    private async Task<BoqProgressResponse> RecordAsync(AuthResponse who, long siteId, object body)
    {
        var response = await Client.PutJsonAsync(Url(siteId), body, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqProgressResponse>();
    }

    private async Task<BoqProgressResponse> ProgressAsync(AuthResponse who, long siteId, string query = "")
    {
        var response = await Client.GetAsync(Url(siteId) + query, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqProgressResponse>();
    }

    private Task<long> EventsAsync(long siteId)
        => _steps.ScalarAsync<long>("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'boq_progress_recorded'", ("@p", siteId));

    [Fact]
    public async Task The_sites_staff_record_progress_and_a_second_record_the_same_day_replaces_the_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (site, client, vo) = await SiteAsync(superAdmin, rami.User.Id);
        var tiling = LineId(client, "Floor tiling");

        // nothing recorded yet: every work line, the permit left out, and no progress rather than 0
        var empty = await ProgressAsync(rami, site.Id);
        empty.Lines.Select(l => l.Description).ShouldBe(["Floor tiling", "Wall painting", "Skirting"]);
        empty.Lines.ShouldAllBe(l => l.ExecutedQuantity == null && l.TakesProgress);
        empty.Progress.ShouldBeNull();
        empty.History.ShouldBeEmpty();

        var first = await RecordAsync(rami, site.Id, new { lines = new[] { new { boqLineId = tiling, executedQuantity = 40m, note = "Ground floor" } } });
        var line = first.Lines.Single(l => l.BoqLineId == tiling);
        (line.ExecutedQuantity, line.ExecutedOn, line.Progress).ShouldBe(((decimal?)40m, (DateOnly?)DateOnly.Parse(Today), (decimal?)0.4m));
        first.History.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            h => h.RecordedBy.Id.ShouldBe(rami.User.Id),
            h => h.Note.ShouldBe("Ground floor"));

        // the same day again replaces it: one record, one more event
        await RecordAsync(rami, site.Id, new { recordedOn = Today, lines = new[] { new { boqLineId = tiling, executedQuantity = 55m } } });
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM boq_line_progress WHERE boq_line_id = @l", ("@l", tiling))).ShouldBe(1);
        (await EventsAsync(site.Id)).ShouldBe(2);
        // and the same figure once more changes nothing
        await RecordAsync(rami, site.Id, new { lines = new[] { new { boqLineId = tiling, executedQuantity = 55m } } });
        (await EventsAsync(site.Id)).ShouldBe(2);

        // an earlier day is its own record; the latest day is the line's quantity, and asOf reads the site as it stood
        await RecordAsync(rami, site.Id, new { recordedOn = DaysAgo(7), lines = new[] { new { boqLineId = tiling, executedQuantity = 20m } } });
        (await ProgressAsync(rami, site.Id)).Lines.Single(l => l.BoqLineId == tiling).ExecutedQuantity.ShouldBe(55m);
        var lastWeek = await ProgressAsync(rami, site.Id, $"?asOf={DaysAgo(3)}");
        lastWeek.Lines.Single(l => l.BoqLineId == tiling).ShouldSatisfyAllConditions(
            l => l.ExecutedQuantity.ShouldBe(20m),
            l => l.ExecutedOn.ShouldBe(DateOnly.Parse(DaysAgo(7))));
        lastWeek.History.ShouldHaveSingleItem();

        // a VO's line takes progress too; quantities only, no money
        await RecordAsync(rami, site.Id, new { lines = new[] { new { boqLineId = LineId(vo, "Skirting"), executedQuantity = 10m } } });
        (await _steps.ScalarAsync<long>(
                "SELECT CAST(JSON_EXTRACT(payload, '$.lines') AS SIGNED) FROM activity_events WHERE project_id = @p AND kind = 'boq_progress_recorded' ORDER BY id DESC LIMIT 1",
                ("@p", site.Id)))
            .ShouldBe(1);
        (await _steps.ScalarAsync<long>(
                "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'boq_progress_recorded' AND amount IS NOT NULL", ("@p", site.Id)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Progress_is_value_executed_over_agreed_value_and_the_money_is_only_for_financial_view()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (site, client, _) = await SiteAsync(superAdmin, rami.User.Id);

        // a pricing manager records too: 50 of 100 m² of tiling at 10 is 500 of the 2,100 agreed
        await RecordAsync(superAdmin, site.Id, new { lines = new[] { new { boqLineId = LineId(client, "Floor tiling"), executedQuantity = 50m } } });

        var withMoney = await ProgressAsync(superAdmin, site.Id);
        (withMoney.AgreedValue, withMoney.ValueExecuted, withMoney.Progress).ShouldBe(((decimal?)2_100m, (decimal?)500m, (decimal?)0.2381m));
        withMoney.Lines.Single(l => l.Description == "Floor tiling").ShouldSatisfyAllConditions(
            l => l.UnitPrice.ShouldBe(10m),
            l => l.AgreedValue.ShouldBe(1_000m),
            l => l.ValueExecuted.ShouldBe(500m));
        withMoney.Lines.Single(l => l.Description == "Wall painting").ValueExecuted.ShouldBe(0m);

        // the site's staff and an Execution admin who follows every site's logs see the quantities and the progress, never a price
        foreach (var reader in new[] { rami, siteAdmin })
        {
            var plain = await ProgressAsync(reader, site.Id);
            plain.Progress.ShouldBe(0.2381m);
            (plain.AgreedValue, plain.ValueExecuted).ShouldBe(((decimal?)null, (decimal?)null));
            plain.Lines.ShouldAllBe(l => l.UnitPrice == null && l.AgreedValue == null && l.ValueExecuted == null);
            plain.Lines.Single(l => l.Description == "Floor tiling").Progress.ShouldBe(0.5m);
        }

        // Financial Intelligence shows the same progress beside the progress percentage, which it leaves as it was
        var insightResponse = await Client.GetAsync($"/api/finance/insights/projects/{site.Id}", superAdmin.AccessToken);
        insightResponse.IsSuccessStatusCode.ShouldBeTrue(await insightResponse.Content.ReadAsStringAsync());
        var insight = await insightResponse.ReadAsync<ProjectInsightResponse>();
        insight.ExecutedProgress.ShouldBe(0.2381m);
        insight.ProgressPct.ShouldBe(site.ProgressPct);
    }

    [Fact]
    public async Task Only_the_sites_own_staff_and_pricing_managers_record_and_other_businesses_never_see_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (site, client, _) = await SiteAsync(superAdmin, rami.User.Id);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [layla.User.Id]);
        var body = new { lines = new[] { new { boqLineId = LineId(client, "Floor tiling"), executedQuantity = 10m } } };

        // staff of another site, and an admin on no site's staff, neither record nor read it as staff
        await (await Client.PutJsonAsync(Url(site.Id), body, layla.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);
        await (await Client.GetAsync(Url(site.Id), layla.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);
        await (await Client.PutJsonAsync(Url(site.Id), body, siteAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);

        // a Design employee holds none of the permissions; a Design admin who sees money doesn't see Execution, so the
        // site isn't there for them
        await (await Client.GetAsync(Url(site.Id), designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var granted = await Client.PutJsonAsync($"/api/users/{designAdmin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "grant", reason = "Design money" } } }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        await (await Client.GetAsync(Url(site.Id), designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a Design project has no BOQs to make progress on
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await (await Client.PutJsonAsync(Url(design.Id), body, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.GetAsync(Url(design.Id), superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);

        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM boq_line_progress WHERE project_id = @p", ("@p", site.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Only_agreed_work_lines_take_progress_up_to_twice_their_quantity_and_up_to_today()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (site, client, _) = await SiteAsync(superAdmin, rami.User.Id);
        var (otherSite, otherClient, _) = await SiteAsync(superAdmin, rami.User.Id);
        var draft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "Not agreed yet");
        draft = await Boqs.SaveAsync(superAdmin.AccessToken, draft.Id, [Line("Extra tiling", 5m, 10m, unit: "m2")], "Variation order", reason: "Not agreed yet");

        Task<HttpResponseMessage> Record(long lineId, decimal quantity, string? day = null)
            => Client.PutJsonAsync(Url(site.Id), new { recordedOn = day, lines = new[] { new { boqLineId = lineId, executedQuantity = quantity } } }, rami.AccessToken);

        // a draft's line, a permit, and another site's line take none
        (await (await Record(LineId(draft, "Extra tiling"), 1m)).ShouldFailWithAsync(400, ErrorCodes.BoqProgressLineInvalid))
            .Errors!.Keys.ShouldBe(["lines[0].boqLineId"]);
        await (await Record(LineId(client, "Building permit"), 1m)).ShouldFailWithAsync(400, ErrorCodes.BoqProgressLineInvalid);
        await (await Record(LineId(otherClient, "Floor tiling"), 1m)).ShouldFailWithAsync(400, ErrorCodes.BoqProgressLineInvalid);

        // up to twice the line's 100 m², to catch a typo; an overrun below that is kept and passes 1
        await (await Record(LineId(client, "Floor tiling"), 200.0001m)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var over = await RecordAsync(rami, site.Id, new { lines = new[] { new { boqLineId = LineId(client, "Floor tiling"), executedQuantity = 120m } } });
        over.Lines.Single(l => l.Description == "Floor tiling").Progress.ShouldBe(1.2m);

        // never a day still to come, nor a negative quantity
        await (await Record(LineId(client, "Wall painting"), 1m, DateOnly.Parse(Today).AddDays(1).ToString("yyyy-MM-dd")))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Record(LineId(client, "Wall painting"), -1m)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM boq_line_progress WHERE project_id = @p", ("@p", otherSite.Id))).ShouldBe(0);
    }
}
