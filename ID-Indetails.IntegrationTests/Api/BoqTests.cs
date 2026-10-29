using System.Text.RegularExpressions;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.IntegrationTests.Api.BoqTestSteps;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Bills of quantities: internal drafts, the client BOQ and variation orders. Every test follows its own project, since
/// the classes share one database.
/// </summary>
public class BoqTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private BoqTestSteps Boqs => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    /// <summary>An Execution admin the super admin granted exactly <paramref name="codes"/>, on top of an admin's defaults.</summary>
    private async Task<AuthResponse> AdminWithAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = codes.Select(code => new { code, action = "grant", reason = "BOQ work" }) }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }

    /// <summary>An Execution site with no work price, the way a new one starts: agreeing a BOQ gives it one.</summary>
    private Task<ProjectResponse> NewSiteAsync(AuthResponse by) => Projects.CreateProjectAsync(by.AccessToken, DomainCodes.Execution, projectFee: 0m);

    /// <summary>10% supervision and 11% TVA on the site, as a new client project starts.</summary>
    private async Task SetRatesAsync(AuthResponse superAdmin, long siteId)
        => await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{siteId}/pricing",
            new { supervisionRate = 0.10m, tvaRate = 0.11m, description = "The usual rates" }, superAdmin.AccessToken));

    private Task<BoqListResponse> ListAsync(AuthResponse by, long siteId) => OkAsync<BoqListResponse>(Client.GetAsync($"/api/projects/{siteId}/boqs", by.AccessToken));

    private async Task<ProjectMoneyResponse> MoneyAsync(AuthResponse by, long siteId)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{siteId}", by.AccessToken))).Money!;

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => _steps.ScalarAsync<long>(sql, parameters);

    private Task<long> EventsAsync(long boqId, string kind)
        => CountAsync("SELECT COUNT(*) FROM activity_events WHERE subject_type = 'boq' AND subject_id = @id AND kind = @kind", ("@id", boqId), ("@kind", kind));

    /// <summary>A site expense written straight into the database: adding one through the API is itself held to the profit floor.</summary>
    private Task CostAsync(long siteId, long userId, decimal amount)
        => _steps.ExecuteAsync(
            "INSERT INTO site_expenses (project_id, name, amount, currency, spent_on, added_at, added_by_id) VALUES (@p, 'Concrete', @a, 'USD', UTC_DATE(), UTC_TIMESTAMP(3), @u)",
            ("@p", siteId), ("@a", amount), ("@u", userId));

    [Fact]
    public async Task An_internal_draft_is_private_to_its_author_and_seen_by_whoever_manages_pricing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var author = await AdminWithAsync(superAdmin, PermissionCodes.BoqCreate);
        var otherDrafter = await AdminWithAsync(superAdmin, PermissionCodes.BoqCreate);
        var pricing = await AdminWithAsync(superAdmin, PermissionCodes.ProjectPricingManage);
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var site = await NewSiteAsync(superAdmin);

        var draft = await Boqs.CreateAsync(author.AccessToken, site.Id, "internal");
        draft.ShouldSatisfyAllConditions(
            d => d.Kind.ShouldBe(BoqKind.Internal),
            d => d.Number.ShouldBeNull(),
            d => d.Status.ShouldBe(BoqStatus.Draft),
            d => d.Title.ShouldBe("Internal draft"),
            d => d.Author.Id.ShouldBe(author.User.Id),
            d => d.Lines.ShouldBeEmpty());
        var saved = await Boqs.SaveAsync(author.AccessToken, draft.Id, [Line("Excavation", 100, 8)], "My estimate");
        // the author sees their own lines and totals, but not the site's pricing (supervision, TVA, the client's total)
        saved.ShouldSatisfyAllConditions(
            d => d.TotalUsd.ShouldBe(800m),
            d => d.SupervisionRate.ShouldBeNull(),
            d => d.SupervisionUsd.ShouldBeNull(),
            d => d.TvaRate.ShouldBeNull(),
            d => d.TvaUsd.ShouldBeNull(),
            d => d.ClientTotalUsd.ShouldBeNull());
        (await Boqs.GetAsync(author.AccessToken, draft.Id)).ClientTotalUsd.ShouldBeNull();
        (await Boqs.GetAsync(pricing.AccessToken, draft.Id)).ClientTotalUsd.ShouldNotBeNull();

        (await ListAsync(author, site.Id)).Internal.Select(b => b.Id).ShouldBe([draft.Id]);
        (await ListAsync(otherDrafter, site.Id)).Internal.ShouldBeEmpty();
        (await ListAsync(viewer, site.Id)).Internal.ShouldBeEmpty();
        (await ListAsync(pricing, site.Id)).Internal.Select(b => b.Id).ShouldBe([draft.Id]);
        (await ListAsync(superAdmin, site.Id)).Internal.Select(b => b.Id).ShouldBe([draft.Id]);

        // another author's draft doesn't exist for them: reading, saving, copying and deleting are all not found
        foreach (var outsider in new[] { otherDrafter, viewer })
        {
            await (await Client.GetAsync($"/api/boqs/{draft.Id}", outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        }

        await (await Client.PutJsonAsync($"/api/boqs/{draft.Id}", new { title = "Mine now", adjustmentType = "none", lines = Array.Empty<object>() }, otherDrafter.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/duplicate", otherDrafter.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/boqs/{draft.Id}", otherDrafter.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "internal", fromBoqId = draft.Id }, otherDrafter.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // whoever manages pricing reads, copies from and edits any of them; it stays its author's
        (await Boqs.GetAsync(pricing.AccessToken, draft.Id)).Lines.ShouldHaveSingleItem().Description.ShouldBe("Excavation");
        var edited = await Boqs.SaveAsync(pricing.AccessToken, draft.Id, [Line("Excavation", 120, 8)], "Corrected");
        edited.ShouldSatisfyAllConditions(d => d.Title.ShouldBe("Corrected"), d => d.Author.Id.ShouldBe(author.User.Id), d => d.TotalUsd.ShouldBe(960m));

        // a private working paper: no events, no notifications, never sent, agreed or withdrawn
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE subject_type = 'boq' AND subject_id = @id", ("@id", draft.Id))).ShouldBe(0);
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/agree", pricing.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqInternal);
        await (await Client.PostJsonAsync($"/api/boqs/{draft.Id}/withdraw-agreement", new { reason = "No" }, pricing.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqInternal);
        await (await Client.PostJsonAsync($"/api/boqs/{draft.Id}/deliveries", new { channel = "download" }, pricing.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqInternal);
        await (await Client.PostJsonAsync($"/api/boqs/{draft.Id}/email", new { to = "client@test.local" }, pricing.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqInternal);
        await (await Client.GetAsync($"/api/boqs/{draft.Id}/pdf", pricing.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqInternal);

        // the author deletes their own; it is gone for everyone
        ((int)(await Client.DeleteAsync($"/api/boqs/{draft.Id}", author.AccessToken)).StatusCode).ShouldBe(204);
        await (await Client.GetAsync($"/api/boqs/{draft.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ListAsync(superAdmin, site.Id)).Internal.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_permission_opens_its_own_part_of_the_BOQs_and_nothing_else()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var drafter = await AdminWithAsync(superAdmin, PermissionCodes.BoqCreate);
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var pricing = await AdminWithAsync(superAdmin, PermissionCodes.ProjectPricingManage);
        var nobody = await AdminWithAsync(superAdmin);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await NewSiteAsync(superAdmin);
        await SetRatesAsync(superAdmin, site.Id);
        var client = await Boqs.CreateAsync(pricing.AccessToken, site.Id);
        await Boqs.SaveAsync(pricing.AccessToken, client.Id, [Line("Works", 1, 1000m)]);
        var own = await Boqs.CreateAsync(drafter.AccessToken, site.Id, "internal");

        // authorized: the pricing manager, and a super admin, write everything; anyone with financial:view reads
        var asViewer = await ListAsync(viewer, site.Id);
        asViewer.Client.Select(b => b.Id).ShouldBe([client.Id]);
        asViewer.Internal.ShouldBeEmpty();
        asViewer.Price.ShouldNotBeNull().WorkUsd.ShouldBe(0m);
        (await Boqs.GetAsync(viewer.AccessToken, client.Id)).Title.ShouldBe("Villa Mounir");
        ((int)(await Client.GetAsync($"/api/boqs/{client.Id}/pdf", viewer.AccessToken)).StatusCode).ShouldBe(200);

        // a drafter sees their own drafts and none of the money
        var asDrafter = await ListAsync(drafter, site.Id);
        asDrafter.Internal.Select(b => b.Id).ShouldBe([own.Id]);
        asDrafter.Client.ShouldBeEmpty();
        asDrafter.Vos.ShouldBeEmpty();
        asDrafter.Price.ShouldBeNull();
        await (await Client.GetAsync($"/api/boqs/{client.Id}", drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/boqs/{client.Id}/pdf", drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // client BOQs and VOs are written with project:pricing_manage alone
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client" }, drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "vo", reason = "More" }, drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/boqs/{client.Id}", new { title = "X", adjustmentType = "none", lines = Array.Empty<object>() }, drafter.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync($"/api/boqs/{client.Id}/duplicate", drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/boqs/{client.Id}", drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/boqs/{client.Id}/copy-lines", new { fromBoqId = own.Id }, drafter.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        foreach (var other in new[] { drafter, viewer, nobody, employee })
        {
            await (await Client.PostEmptyAsync($"/api/boqs/{client.Id}/agree", other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"/api/boqs/{client.Id}/withdraw-agreement", new { reason = "No" }, other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"/api/boqs/{client.Id}/deliveries", new { channel = "download" }, other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"/api/boqs/{client.Id}/email", new { to = "client@test.local" }, other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }

        // a viewer writes no draft at all, and someone with none of them reads and writes nothing
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "internal" }, viewer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        foreach (var other in new[] { nobody, employee })
        {
            await (await Client.GetAsync($"/api/projects/{site.Id}/boqs", other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.GetAsync($"/api/boqs/{client.Id}", other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "internal" }, other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }

        // someone writing drafts can't make a client BOQ even by copying: the pricing manager can write them all
        (await Boqs.CreateAsync(pricing.AccessToken, site.Id, "client", fromBoqId: client.Id)).Lines.ShouldHaveSingleItem();
        (await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "client", fromBoqId: own.Id)).Lines.ShouldBeEmpty();

        // the domain is invisible to another one: its money admin finds neither the project nor its BOQs
        await (await Client.GetAsync($"/api/projects/{site.Id}/boqs", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/boqs/{client.Id}", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/boqs/{client.Id}/pdf", designMoney.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // the permissions to write them belong to Execution, so a Design admin is refused before any lookup
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client" }, designMoney.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync($"/api/boqs/{client.Id}/agree", designMoney.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/projects/999999999/boqs", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/boqs/999999999", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Only_an_execution_client_project_has_BOQs()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.GetAsync($"/api/projects/{design.Id}/boqs", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqExecutionOnly);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/boqs", new { kind = "client" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqExecutionOnly);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/boqs", new { kind = "internal" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqExecutionOnly);
    }

    [Fact]
    public async Task Client_BOQs_and_VOs_are_numbered_by_prefix_and_year_and_drafts_are_not()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);

        var first = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        var second = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        var internalDraft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "internal");

        var year = DateTime.UtcNow.Year;
        Regex.IsMatch(first.Number!, $@"^BQ-({year}|{year - 1}|{year + 1})-\d{{3,}}$").ShouldBeTrue(first.Number);
        int Sequence(string number) => int.Parse(number[^(number.Length - number.LastIndexOf('-') - 1)..]);
        Sequence(second.Number!).ShouldBeGreaterThan(Sequence(first.Number!));
        second.Title.ShouldBe("Option B");
        first.Title.ShouldBe("Client BOQ");
        internalDraft.Number.ShouldBeNull();

        await Boqs.SaveAsync(superAdmin.AccessToken, first.Id, [Line("Works", 1, 500m)]);
        await Boqs.AgreeAsync(superAdmin.AccessToken, first.Id);
        var vo = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "An extra room");
        Regex.IsMatch(vo.Number!, @"^VO-\d{4}-\d{3,}$").ShouldBeTrue(vo.Number);
        vo.Title.ShouldBe("Variation order");
        vo.Reason.ShouldBe("An extra room");

        // a duplicate takes the next number of its prefix; a number is never reused
        var copy = await OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{first.Id}/duplicate", superAdmin.AccessToken));
        Sequence(copy.Number!).ShouldBeGreaterThan(Sequence(second.Number!));
        (await CountAsync("SELECT COUNT(DISTINCT number) FROM boqs WHERE project_id = @p AND number IS NOT NULL", ("@p", site.Id))).ShouldBe(4);

        var created = await EventsAsync(first.Id, ActivityEventKinds.BoqCreated);
        created.ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'boq_created'", ("@p", site.Id))).ShouldBe(4);
    }

    [Fact]
    public async Task A_saved_BOQ_is_priced_by_the_server_with_waste_an_adjustment_supervision_and_tva()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await SetRatesAsync(superAdmin, site.Id);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);

        // 10 + 5% waste = 10.50 × 100 = 1,050; 2 × 25 = 50; 10% off the 1,100
        var saved = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id,
            [Line("Concrete slab", 10, 100m, waste: 0.05m, code: "2.1.1", unit: "m3"), Line("Formwork", 2, 25m, code: "2.1.2", unit: "m2")],
            adjustmentType: "percent", adjustmentValue: -0.10m);

        saved.Lines.Select(l => (l.Position, l.Code, l.Quantity, l.WasteRate, l.FinalQuantity, l.Rate, l.TotalUsd)).ShouldBe(
        [
            (1, "2.1.1", 10m, 0.05m, 10.5m, 100m, 1050m),
            (2, "2.1.2", 2m, 0m, 2m, 25m, 50m),
        ]);
        saved.ShouldSatisfyAllConditions(
            b => b.SubtotalUsd.ShouldBe(1100m),
            b => b.AdjustmentType.ShouldBe(BoqAdjustmentType.Percent),
            b => b.AdjustmentValue.ShouldBe(-0.10m),
            b => b.AdjustmentUsd.ShouldBe(-110m),
            b => b.TotalUsd.ShouldBe(990m),
            b => b.LineCount.ShouldBe(2),
            b => b.SupervisionRate.ShouldBe(0.10m),
            b => b.SupervisionUsd.ShouldBe(99m),
            b => b.TvaRate.ShouldBe(0.11m),
            // TVA is on the work only, never on supervision
            b => b.TvaUsd.ShouldBe(108.90m),
            b => b.ClientTotalUsd.ShouldBe(1197.90m));

        // by a new total: the adjustment is whatever gets there
        var pinned = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id,
            [Line("Concrete slab", 10, 100m, waste: 0.05m), Line("Formwork", 2, 25m)], adjustmentType: "total", adjustmentValue: 1000m);
        (pinned.AdjustmentUsd, pinned.TotalUsd, pinned.SubtotalUsd).ShouldBe((-100m, 1000m, 1100m));

        // none again
        var none = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("Concrete slab", 10, 100m)]);
        (none.AdjustmentType, none.AdjustmentValue, none.AdjustmentUsd, none.TotalUsd).ShouldBe((BoqAdjustmentType.None, null, 0m, 1000m));
    }

    [Fact]
    public async Task A_save_made_from_an_older_copy_is_refused_so_it_cannot_undo_someone_elses()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var opened = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);

        // two people open the same BOQ, and the first saves
        var first = await Boqs.SaveAsync(superAdmin.AccessToken, opened.Id, [Line("Walls", 10, 50m)], updatedAt: opened.UpdatedAt);

        // the second, still on the copy they opened, is refused, and nothing of theirs is kept
        await (await Client.PutJsonAsync($"/api/boqs/{opened.Id}",
                new { title = "Mine", adjustmentType = "none", lines = new[] { Line("Floors", 5, 20m) }, updatedAt = opened.UpdatedAt }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.BoqChanged);
        (await Boqs.GetAsync(superAdmin.AccessToken, opened.Id)).Lines.ShouldHaveSingleItem().Description.ShouldBe("Walls");

        // saving from the copy each save answers with goes through, save after save
        var second = await Boqs.SaveAsync(superAdmin.AccessToken, opened.Id, [Line("Walls", 12, 50m)], updatedAt: first.UpdatedAt);
        (await Boqs.SaveAsync(superAdmin.AccessToken, opened.Id, [Line("Walls", 14, 50m)], updatedAt: second.UpdatedAt)).TotalUsd.ShouldBe(700m);
    }

    [Fact]
    public async Task Saving_replaces_the_lines_in_the_order_given_keeping_the_ones_it_names()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        var first = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("One", 1, 10m), Line("Two", 1, 20m), Line("Three", 1, 30m)]);
        var (one, two, three) = (first.Lines[0], first.Lines[1], first.Lines[2]);

        // three first, a new line, one kept: two is left out and goes
        var second = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id,
            [Line("Three, edited", 2, 30m, id: three.Id), Line("New", 1, 5m), Line("One", 1, 10m, id: one.Id)]);

        second.Lines.Select(l => (l.Position, l.Description)).ShouldBe([(1, "Three, edited"), (2, "New"), (3, "One")]);
        second.Lines[0].Id.ShouldBe(three.Id);
        second.Lines[2].Id.ShouldBe(one.Id);
        second.Lines.ShouldNotContain(l => l.Id == two.Id);
        second.TotalUsd.ShouldBe(75m);
        (await CountAsync("SELECT COUNT(*) FROM boq_lines WHERE boq_id = @b", ("@b", boq.Id))).ShouldBe(3);

        // a line of another BOQ can't be named
        var other = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Other");
        var theirs = (await Boqs.SaveAsync(superAdmin.AccessToken, other.Id, [Line("Theirs", 1, 1m)])).Lines.Single();
        await (await Client.PutJsonAsync($"/api/boqs/{boq.Id}",
                new { title = "X", adjustmentType = "none", lines = new[] { Line("Theirs", 1, 1m, id: theirs.Id) } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await Boqs.GetAsync(superAdmin.AccessToken, boq.Id)).Lines.Count.ShouldBe(3);
    }

    [Fact]
    public async Task What_a_save_may_hold_is_checked_by_the_shape_of_the_request_and_by_the_kind_of_BOQ()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        Task<HttpResponseMessage> Save(object body) => Client.PutJsonAsync($"/api/boqs/{boq.Id}", body, superAdmin.AccessToken);
        object[] lines(params object[] given) => given;

        // shape: 400 with the field
        (await (await Save(new { title = "", adjustmentType = "none", lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("title");
        (await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line(" ", 1, 1m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("lines[0].description");
        (await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line("A", -1, 1m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("lines[0].quantity");
        (await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line("A", 1, 1m, waste: 1.5m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("lines[0].wasteRate");
        (await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line("A", 1, 1.00001m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("lines[0].rate");
        (await (await Save(new { title = "X", adjustmentType = "none", adjustmentValue = 0.1m, lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("adjustmentValue");
        (await (await Save(new { title = "X", adjustmentType = "percent", lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("adjustmentValue");
        (await (await Save(new { title = "X", adjustmentType = "percent", adjustmentValue = -1m, lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("adjustmentValue");
        (await (await Save(new { title = "X", adjustmentType = "percent", adjustmentValue = 1.01m, lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("adjustmentValue");
        await (await Save(new { title = "X", adjustmentType = "sideways", lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await (await Save(new { title = "X", adjustmentType = "none", lines = Enumerable.Repeat(Line("A", 1, 1m), 1001).ToArray() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed))
            .Errors!.Keys.ShouldContain("lines");
        (await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "vo" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed))
            .Errors!.Keys.ShouldContain("reason");
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client", reason = "Why" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "contract" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // kind: only a VO has a negative rate, a negative total, or a reason
        await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line("Credit", 1, -50m)) })).ShouldFailWithAsync(422, ErrorCodes.BoqNegativeRate);
        await (await Save(new { title = "X", adjustmentType = "total", adjustmentValue = -5m, lines = lines(Line("A", 1, 10m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Save(new { title = "X", reason = "Why", adjustmentType = "none", lines = lines() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        // the total has to fit what is stored
        await (await Save(new { title = "X", adjustmentType = "none", lines = lines(Line("Huge", 999_999_999m, 999_999_999m)) })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await Boqs.GetAsync(superAdmin.AccessToken, boq.Id)).Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Agreeing_the_client_BOQ_sets_the_work_price_and_everything_that_follows_from_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await SetRatesAsync(superAdmin, site.Id);
        (await MoneyAsync(superAdmin, site.Id)).ShouldSatisfyAllConditions(m => m.ProjectFee.ShouldBe(0m), m => m.Revenue.ShouldBe(0m), m => m.RevenueWithTva.ShouldBe(0m));

        var draft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, draft.Id, [Line("Whole building", 1, 10_000m, unit: "lot")]);
        // the price isn't the work price until the BOQ is agreed
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(0m);
        (await ListAsync(superAdmin, site.Id)).Price!.WorkUsd.ShouldBe(0m);

        var agreed = await Boqs.AgreeAsync(superAdmin.AccessToken, draft.Id);
        agreed.ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe(BoqStatus.Agreed),
            b => b.AgreedAt.ShouldNotBeNull(),
            b => b.AgreedBy!.Id.ShouldBe(superAdmin.User.Id),
            b => b.ClientTotalUsd.ShouldBe(12_100m));

        var money = await MoneyAsync(superAdmin, site.Id);
        money.ShouldSatisfyAllConditions(
            m => m.ProjectFee.ShouldBe(10_000m),
            m => m.SupervisionUsd.ShouldBe(1000m),
            // revenue is the work plus supervision; TVA is on the work only
            m => m.Revenue.ShouldBe(11_000m),
            m => m.RevenueWithTva.ShouldBe(12_100m),
            m => m.PaymentWithTva.ShouldBe(12_100m));
        (await ListAsync(superAdmin, site.Id)).Price.ShouldBe(new BoqPriceResponse(10_000m, 0m, 10_000m, 0.10m, 1000m, 0.11m, 1100m, 12_100m));

        // it is written like a fee change always was: in the payment history, with the BOQ's number as the reason, and in the feed
        var history = await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken));
        history[0].ShouldSatisfyAllConditions(
            h => h.Amount.ShouldBe(10_000m),
            h => h.PreviousAmount.ShouldBe(0m),
            h => h.Description.ShouldBe(agreed.Number),
            h => h.ChangedBy!.Id.ShouldBe(superAdmin.User.Id),
            h => h.TvaRate.ShouldBe(0.11m),
            h => h.PreviousTvaRate.ShouldBe(0.11m),
            h => h.SupervisionRate.ShouldBe(0.10m),
            h => h.PreviousSupervisionRate.ShouldBe(0.10m));
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'project_fee_set'", ("@p", site.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<decimal>("SELECT amount FROM activity_events WHERE project_id = @p AND kind = 'project_fee_set'", ("@p", site.Id))).ShouldBe(10_000m);
        (await _steps.ScalarAsync<decimal>("SELECT prev_amount FROM activity_events WHERE project_id = @p AND kind = 'project_fee_set'", ("@p", site.Id))).ShouldBe(0m);
        (await EventsAsync(draft.Id, ActivityEventKinds.BoqAgreed)).ShouldBe(1);
        (await _steps.ScalarAsync<decimal>("SELECT amount FROM activity_events WHERE subject_type = 'boq' AND subject_id = @id AND kind = 'boq_agreed'", ("@id", draft.Id))).ShouldBe(10_000m);

        // an agreed BOQ is locked, and only one client BOQ is agreed
        await (await Client.PutJsonAsync($"/api/boqs/{draft.Id}", new { title = "X", adjustmentType = "none", lines = Array.Empty<object>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.BoqLocked);
        await (await Client.PostJsonAsync($"/api/boqs/{draft.Id}/copy-lines", new { fromBoqId = draft.Id }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqLocked);
        await (await Client.DeleteAsync($"/api/boqs/{draft.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqLocked);
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqLocked);

        var other = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        await Boqs.SaveAsync(superAdmin.AccessToken, other.Id, [Line("Cheaper", 1, 8000m)]);
        await (await Client.PostEmptyAsync($"/api/boqs/{other.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqAlreadyAgreed);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(10_000m);
        (await Boqs.GetAsync(superAdmin.AccessToken, other.Id)).Status.ShouldBe(BoqStatus.Draft);
    }

    [Fact]
    public async Task A_BOQ_with_no_lines_is_neither_agreed_nor_sent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var empty = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);

        await (await Client.PostEmptyAsync($"/api/boqs/{empty.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqEmpty);
        await (await Client.PostJsonAsync($"/api/boqs/{empty.Id}/deliveries", new { channel = "whatsapp" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqEmpty);
        var before = api.Emails.CountTo("empty@test.local");
        await (await Client.PostJsonAsync($"/api/boqs/{empty.Id}/email", new { to = "empty@test.local" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqEmpty);
        api.Emails.CountTo("empty@test.local").ShouldBe(before);
    }

    [Fact]
    public async Task A_VO_needs_the_client_BOQ_agreed_and_each_agreed_VO_moves_the_price_by_its_total()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Works", 1, 10_000m)]);

        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "vo", reason = "Too early" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.BoqNotAgreedYet);
        await Boqs.AgreeAsync(superAdmin.AccessToken, client.Id);

        var extra = await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, 1500m, "A guest house");
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(11_500m);
        var credit = await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, -500m, "A simpler facade");
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(11_000m);
        credit.TotalUsd.ShouldBe(-500m);
        credit.Reason.ShouldBe("A simpler facade");
        (await ListAsync(superAdmin, site.Id)).Price.ShouldBe(new BoqPriceResponse(10_000m, 1000m, 11_000m, 0m, 0m, 0m, 0m, 11_000m));
        (await ListAsync(superAdmin, site.Id)).Vos.Select(v => v.Id).ShouldBe([credit.Id, extra.Id]);

        // each VO is a row of the history, with the difference it made
        var history = await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken));
        history.Take(2).Select(h => (h.Amount, h.PreviousAmount, h.Description)).ShouldBe([(11_000m, (decimal?)11_500m, credit.Number!), (11_500m, 10_000m, extra.Number!)]);

        // a VO that would take the price below nothing is refused outright
        var tooMuch = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "Everything back");
        await Boqs.SaveAsync(superAdmin.AccessToken, tooMuch.Id, [Line("Credit", 1, -20_000m)], reason: "Everything back");
        await (await Client.PostEmptyAsync($"/api/boqs/{tooMuch.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqPriceBelowZero);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(11_000m);

        // only a VO can hold a negative rate, so a client BOQ can't be copied from one
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client", fromBoqId = credit.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.BoqNegativeRate);
    }

    [Fact]
    public async Task Withdrawing_returns_the_BOQ_to_draft_with_a_reason_and_takes_it_out_of_the_price()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 10_000m);
        var vo = await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, 2000m, "A garage");
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(12_000m);
        string Url(BoqDetailResponse b) => $"/api/boqs/{b.Id}/withdraw-agreement";

        // a reason is required; a draft has nothing to withdraw; the client BOQ stays while a VO stands on it
        (await (await Client.PostJsonAsync(Url(vo), new { reason = "  " }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("reason");
        await (await Client.PostJsonAsync(Url(client), new { reason = "The client changed their mind" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqHasAgreedVos);
        var draft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        await (await Client.PostJsonAsync(Url(draft), new { reason = "Never agreed" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqNotAgreed);

        var withdrawnVo = await OkAsync<BoqDetailResponse>(Client.PostJsonAsync(Url(vo), new { reason = "The garage was dropped" }, superAdmin.AccessToken));
        withdrawnVo.ShouldSatisfyAllConditions(b => b.Status.ShouldBe(BoqStatus.Draft), b => b.AgreedAt.ShouldBeNull(), b => b.AgreedBy.ShouldBeNull(), b => b.Number.ShouldBe(vo.Number));
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(10_000m);

        // taking the whole price away leaves no profit, so it is held to the floor and forced; the VO made while the client BOQ stood
        // then needs the client BOQ agreed again before it can be agreed
        var refused = await (await Client.PostJsonAsync(Url(client), new { reason = "Starting over" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{refused.BlockedChangeId}/force", superAdmin.AccessToken));
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(0m);
        await (await Client.PostEmptyAsync($"/api/boqs/{vo.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqNotAgreedYet);

        // it is a draft again: editable, and it can be agreed again
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Whole building", 1, 9000m)]);
        await Boqs.AgreeAsync(superAdmin.AccessToken, client.Id);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(9000m);

        var history = await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken));
        history.Select(h => (h.Amount, h.PreviousAmount, h.Description)).Take(5).ShouldBe(
        [
            (9000m, (decimal?)0m, client.Number!),
            (0m, 10_000m, "Starting over"),
            (10_000m, 12_000m, "The garage was dropped"),
            (12_000m, 10_000m, vo.Number!),
            (10_000m, 0m, client.Number!),
        ]);
        (await EventsAsync(vo.Id, ActivityEventKinds.BoqAgreementWithdrawn)).ShouldBe(1);
        (await _steps.ScalarAsync<string>("SELECT payload FROM activity_events WHERE subject_type = 'boq' AND subject_id = @id AND kind = 'boq_agreement_withdrawn'", ("@id", vo.Id)))
            .ShouldContain("The garage was dropped");
    }

    [Fact]
    public async Task A_lowering_agreement_that_takes_the_profit_to_zero_is_kept_for_forcing_and_a_rise_never_is()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);
        await CostAsync(site.Id, superAdmin.User.Id, 800m);

        // 50 of profit is close to zero, not at it: allowed
        await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, -150m);
        (await MoneyAsync(superAdmin, site.Id)).Profit.ShouldBe(50m);

        // taking it past zero is refused, and kept with the request
        var vo = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "A cheaper finish");
        await Boqs.SaveAsync(superAdmin.AccessToken, vo.Id, [Line("Credit", 1, -100m)], "Variation order", reason: "A cheaper finish");
        var refused = await (await Client.PostEmptyAsync($"/api/boqs/{vo.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = refused.BlockedChangeId.ShouldNotBeNull();
        (await Boqs.GetAsync(superAdmin.AccessToken, vo.Id)).Status.ShouldBe(BoqStatus.Draft);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(850m);

        var blocked = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", superAdmin.AccessToken));
        blocked.ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BlockedChangeKind.BoqAgree),
            b => b.Status.ShouldBe(BlockedChangeStatus.Pending),
            b => b.Summary.ShouldBe($"Agreeing {vo.Number}"),
            b => b.ProjectId.ShouldBe(site.Id),
            b => b.ProfitNow.ShouldBe(50m));

        // forcing it replays the agreement past the floor, once
        var forced = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken));
        forced.ShouldSatisfyAllConditions(b => b.Status.ShouldBe(BlockedChangeStatus.Forced), b => b.ProfitNow.ShouldBe(-50m));
        (await Boqs.GetAsync(superAdmin.AccessToken, vo.Id)).Status.ShouldBe(BoqStatus.Agreed);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(750m);
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{id}/force", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BlockedChangeDecided);

        // a rise passes, even with the profit below zero
        await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, 500m, "A new room");
        (await MoneyAsync(superAdmin, site.Id)).ShouldSatisfyAllConditions(m => m.ProjectFee.ShouldBe(1250m), m => m.Profit.ShouldBe(450m));
    }

    [Fact]
    public async Task Withdrawing_the_price_away_is_held_to_the_floor_and_can_be_dismissed_or_forced()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);
        await CostAsync(site.Id, superAdmin.User.Id, 600m);
        var url = $"/api/boqs/{client.Id}/withdraw-agreement";

        var refused = await (await Client.PostJsonAsync(url, new { reason = "Starting over" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var id = refused.BlockedChangeId.ShouldNotBeNull();
        (await Boqs.GetAsync(superAdmin.AccessToken, client.Id)).Status.ShouldBe(BoqStatus.Agreed);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(1000m);
        (await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{id}", superAdmin.AccessToken))).ShouldSatisfyAllConditions(
            b => b.Kind.ShouldBe(BlockedChangeKind.BoqWithdraw),
            b => b.Summary.ShouldBe($"Withdrawing the agreement of {client.Number}: Starting over"));

        var dismissed = await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{id}/dismiss", superAdmin.AccessToken));
        dismissed.Status.ShouldBe(BlockedChangeStatus.Dismissed);
        (await Boqs.GetAsync(superAdmin.AccessToken, client.Id)).Status.ShouldBe(BoqStatus.Agreed);

        var again = await (await Client.PostJsonAsync(url, new { reason = "Really starting over" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        await OkAsync<BlockedChangeResponse>(Client.PostEmptyAsync($"/api/blocked-changes/{again.BlockedChangeId}/force", superAdmin.AccessToken));
        (await Boqs.GetAsync(superAdmin.AccessToken, client.Id)).Status.ShouldBe(BoqStatus.Draft);
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(0m);
        (await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken)))[0]
            .Description.ShouldBe("Really starting over");
    }

    [Fact]
    public async Task A_first_agreement_passes_even_when_it_does_not_cover_the_costs_already_recorded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await CostAsync(site.Id, superAdmin.User.Id, 1200m);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("Works", 1, 1000m)]);

        // the floor is off until a price is agreed, and agreeing one raises the price: a rise never needs forcing
        await OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{boq.Id}/agree", superAdmin.AccessToken));
        (await MoneyAsync(superAdmin, site.Id)).ProjectFee.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_draft_can_be_sent_as_often_as_wanted_and_each_send_keeps_the_total_it_had()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("Concrete slab", 10, 120.5m, waste: 0.05m, code: "2.1.1", unit: "m3")]);

        var sent = await OkAsync<BoqDetailResponse>(Client.PostJsonAsync($"/api/boqs/{boq.Id}/deliveries",
            new { channel = "whatsapp", recipient = "  Mr Haddad  ", note = "By hand" }, superAdmin.AccessToken));
        sent.Deliveries.Single().ShouldSatisfyAllConditions(
            d => d.Channel.ShouldBe(DeliveryChannel.Whatsapp),
            d => d.Recipient.ShouldBe("Mr Haddad"),
            d => d.Note.ShouldBe("By hand"),
            d => d.TotalUsd.ShouldBe(1265.25m),
            d => d.SentBy!.Id.ShouldBe(superAdmin.User.Id));
        sent.LastDelivery!.Channel.ShouldBe(DeliveryChannel.Whatsapp);

        // a draft stays editable after it was sent: the delivery keeps what was sent
        var edited = await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("Concrete slab", 10, 130m, waste: 0.05m)]);
        edited.TotalUsd.ShouldBe(1365m);
        edited.Deliveries.Single().TotalUsd.ShouldBe(1265.25m);

        // by email: recorded as an email, with the PDF attached and the figures the client reads
        await (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/email", new { to = "not an email" }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var emailed = await OkAsync<BoqDetailResponse>(Client.PostJsonAsync($"/api/boqs/{boq.Id}/email",
            new { to = " site-boq@test.local ", note = "Second send" }, superAdmin.AccessToken));
        emailed.Deliveries.Count.ShouldBe(2);
        emailed.Deliveries[0].ShouldSatisfyAllConditions(
            d => d.Channel.ShouldBe(DeliveryChannel.Email),
            d => d.Recipient.ShouldBe("site-boq@test.local"),
            d => d.Note.ShouldBe("Second send"),
            d => d.TotalUsd.ShouldBe(1365m));
        emailed.LastDelivery!.Channel.ShouldBe(DeliveryChannel.Email);

        var email = api.Emails.LastTo("site-boq@test.local");
        email.Subject.ShouldBe($"Bill of quantities {boq.Number} - {site.Title}");
        email.Attachments!.Single().FileName.ShouldBe(boq.Number!.ToLowerInvariant() + ".pdf");
        email.Attachments!.Single().MimeType.ShouldBe("application/pdf");
        // the client reads the final quantity, never the quantity before waste nor the waste itself
        email.TextBody.ShouldContain("10.5 m3");
        email.TextBody.ShouldContain("2.1.1");
        email.TextBody.ShouldNotContain("0.05");
        email.TextBody.ShouldNotContain("waste", Case.Insensitive);

        // a send that fails records nothing
        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/email", new { to = "site-boq@test.local" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);
        (await Boqs.GetAsync(superAdmin.AccessToken, boq.Id)).Deliveries.Count.ShouldBe(2);
        (await EventsAsync(boq.Id, ActivityEventKinds.BoqSent)).ShouldBe(2);

        // an agreed BOQ is sent too, and deleting a sent draft keeps what was sent on record
        var other = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        await Boqs.SaveAsync(superAdmin.AccessToken, other.Id, [Line("Works", 1, 10m)]);
        await Client.PostJsonAsync($"/api/boqs/{other.Id}/deliveries", new { channel = "download" }, superAdmin.AccessToken);
        ((int)(await Client.DeleteAsync($"/api/boqs/{other.Id}", superAdmin.AccessToken)).StatusCode).ShouldBe(204);
        (await CountAsync("SELECT COUNT(*) FROM boq_deliveries WHERE boq_id = @b", ("@b", other.Id))).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) FROM boqs WHERE id = @b AND deleted_at IS NOT NULL", ("@b", other.Id))).ShouldBe(1);
        await (await Client.GetAsync($"/api/boqs/{other.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ListAsync(superAdmin, site.Id)).Client.Select(b => b.Id).ShouldBe([boq.Id]);
    }

    [Fact]
    public async Task The_PDF_of_a_client_BOQ_or_a_VO_is_downloaded_by_whoever_reads_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 4000m);
        var vo = await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, -300m);

        foreach (var (boq, token) in new[] { (client, superAdmin.AccessToken), (vo, viewer.AccessToken) })
        {
            var response = await Client.GetAsync($"/api/boqs/{boq.Id}/pdf", token);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe(boq.Number!.ToLowerInvariant() + ".pdf");
            System.Text.Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync(), 0, 5).ShouldBe("%PDF-");
        }
    }

    [Fact]
    public async Task A_finished_site_takes_no_BOQ_writes_until_it_is_reopened_but_can_still_be_read()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var boq = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, boq.Id, [Line("Works", 1, 500m)]);
        var done = await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken);
        done.IsSuccessStatusCode.ShouldBeTrue(await done.Content.ReadAsStringAsync());

        (await Boqs.GetAsync(superAdmin.AccessToken, boq.Id)).Lines.ShouldHaveSingleItem();
        (await ListAsync(superAdmin, site.Id)).Client.ShouldHaveSingleItem();
        var before = api.Emails.CountTo("closed@test.local");
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PutJsonAsync($"/api/boqs/{boq.Id}", new { title = "X", adjustmentType = "none", lines = Array.Empty<object>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostEmptyAsync($"/api/boqs/{boq.Id}/agree", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostEmptyAsync($"/api/boqs/{boq.Id}/duplicate", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.DeleteAsync($"/api/boqs/{boq.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/deliveries", new { channel = "download" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/email", new { to = "closed@test.local" }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        api.Emails.CountTo("closed@test.local").ShouldBe(before);

        await Client.PostEmptyAsync($"/api/projects/{site.Id}/reopen", superAdmin.AccessToken);
        (await Boqs.AgreeAsync(superAdmin.AccessToken, boq.Id)).Status.ShouldBe(BoqStatus.Agreed);
    }

    [Fact]
    public async Task A_BOQ_is_duplicated_and_its_lines_copied_into_another_with_the_server_working_out_the_totals()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var drafter = await AdminWithAsync(superAdmin, PermissionCodes.BoqCreate);
        var site = await NewSiteAsync(superAdmin);
        var other = await NewSiteAsync(superAdmin);
        var source = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Villa");
        var saved = await Boqs.SaveAsync(superAdmin.AccessToken, source.Id,
            [Line("Slab", 10, 100m, waste: 0.05m, code: "1"), Line("Walls", 4, 50m, code: "2"), Line("Roof", 1, 300m, code: "3")],
            "Villa", "percent", -0.10m);

        var copy = await OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{source.Id}/duplicate", superAdmin.AccessToken));
        copy.ShouldSatisfyAllConditions(
            c => c.Id.ShouldNotBe(source.Id),
            c => c.Number.ShouldNotBe(source.Number),
            c => c.Title.ShouldBe("Villa (copy)"),
            c => c.Status.ShouldBe(BoqStatus.Draft),
            c => c.Kind.ShouldBe(BoqKind.Client),
            c => c.AdjustmentType.ShouldBe(BoqAdjustmentType.Percent),
            c => c.AdjustmentValue.ShouldBe(-0.10m),
            c => c.TotalUsd.ShouldBe(saved.TotalUsd),
            c => c.Lines.Select(l => (l.Description, l.WasteRate)).ShouldBe(saved.Lines.Select(l => (l.Description, l.WasteRate))),
            c => c.Lines.Select(l => l.Id).ShouldNotContain(saved.Lines[0].Id));

        // copying lines appends them, in the source's order, to a document of the same project
        var target = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        await Boqs.SaveAsync(superAdmin.AccessToken, target.Id, [Line("Existing", 1, 10m)]);
        var some = await OkAsync<BoqDetailResponse>(Client.PostJsonAsync($"/api/boqs/{target.Id}/copy-lines",
            new { fromBoqId = source.Id, lineIds = new[] { saved.Lines[2].Id, saved.Lines[0].Id } }, superAdmin.AccessToken));
        some.Lines.Select(l => (l.Position, l.Description)).ShouldBe([(1, "Existing"), (2, "Slab"), (3, "Roof")]);
        some.TotalUsd.ShouldBe(10m + 1050m + 300m);
        var all = await OkAsync<BoqDetailResponse>(Client.PostJsonAsync($"/api/boqs/{target.Id}/copy-lines", new { fromBoqId = source.Id }, superAdmin.AccessToken));
        all.Lines.Count.ShouldBe(6);
        all.Lines.Select(l => l.Position).ShouldBe([1, 2, 3, 4, 5, 6]);

        await (await Client.PostJsonAsync($"/api/boqs/{target.Id}/copy-lines", new { fromBoqId = source.Id, lineIds = new[] { 999_999_999L } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var elsewhere = await Boqs.CreateAsync(superAdmin.AccessToken, other.Id);
        await (await Client.PostJsonAsync($"/api/boqs/{target.Id}/copy-lines", new { fromBoqId = elsewhere.Id }, superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client", fromBoqId = elsewhere.Id }, superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a client BOQ starts from an internal draft, and the drafter's own copy is theirs
        var draft = await Boqs.CreateAsync(drafter.AccessToken, site.Id, "internal");
        await Boqs.SaveAsync(drafter.AccessToken, draft.Id, [Line("Excavation", 100, 8m)], "Estimate");
        var fromDraft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "client", fromBoqId: draft.Id);
        (fromDraft.Lines.Single().Description, fromDraft.TotalUsd, fromDraft.Author.Id).ShouldBe(("Excavation", 800m, superAdmin.User.Id));
        var draftCopy = await OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{draft.Id}/duplicate", drafter.AccessToken));
        (draftCopy.Kind, draftCopy.Number, draftCopy.Author.Id, draftCopy.Title).ShouldBe((BoqKind.Internal, null, drafter.User.Id, "Estimate (copy)"));

        // a VO is duplicated with its reason, once the client BOQ stands
        await Boqs.AgreeAsync(superAdmin.AccessToken, source.Id);
        var vo = await Boqs.AgreeVoAsync(superAdmin.AccessToken, site.Id, 100m, "A balcony");
        var voCopy = await OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{vo.Id}/duplicate", superAdmin.AccessToken));
        (voCopy.Kind, voCopy.Reason, voCopy.Status, voCopy.TotalUsd).ShouldBe((BoqKind.Vo, "A balcony", BoqStatus.Draft, 100m));
    }
}
