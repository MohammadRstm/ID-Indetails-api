using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Shouldly;
using static ID_Indetails.IntegrationTests.Api.BoqTestSteps;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// An Execution site's BOQs and variation orders, its pricing and a project's billing, proposed by Proton with a scripted
/// model: every card is strong, the rates and amounts are the user's own words, each change goes through the BOQ and project
/// services only on Confirm, a card made stale is refused, and the one that emails a client reaches only an address the user
/// typed. Every test follows its own site, since the classes share one database.
/// </summary>
public class ProtonBoqActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private BoqTestSteps Boqs => new(_steps);

    /// <param name="said">What the user typed: amounts, rates and addresses on a card must come from it.</param>
    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private Task<HttpResponseMessage> TryConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction!.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private static string Message(JsonNode result) => result["message"]!.GetValue<string>();

    /// <summary>An Execution site with no work price, the way a new one starts, at 10% supervision and 11% TVA.</summary>
    private async Task<ProjectResponse> NewSiteAsync(AuthResponse superAdmin)
    {
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { supervisionRate = 0.10m, tvaRate = 0.11m, description = "The usual rates" }, superAdmin.AccessToken));
        return site;
    }

    private Task<BoqListResponse> ListAsync(AuthResponse by, long siteId) => OkAsync<BoqListResponse>(Client.GetAsync($"/api/projects/{siteId}/boqs", by.AccessToken));

    private Task<BoqDetailResponse> GetBoqAsync(AuthResponse by, long boqId) => Boqs.GetAsync(by.AccessToken, boqId);

    private async Task<ProjectResponse> ProjectAsync(AuthResponse by, long id) => await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{id}", by.AccessToken));

    /// <summary>A client BOQ saved with two lines: a slab of 10 at 100 (1,000) and walls of 4 at 50 (200).</summary>
    private async Task<BoqDetailResponse> VillaAsync(AuthResponse by, long siteId)
    {
        var boq = await Boqs.CreateAsync(by.AccessToken, siteId, title: "Villa Mounir");
        return await Boqs.SaveAsync(by.AccessToken, boq.Id, [Line("Slab", 10, 100m, code: "1", unit: "m3"), Line("Walls", 4, 50m, code: "2", unit: "m2")], "Villa Mounir");
    }

    /// <summary>A site expense written straight into the database: adding one through the API is itself held to the profit floor.</summary>
    private Task CostAsync(long siteId, long userId, decimal amount)
        => _steps.ExecuteAsync(
            "INSERT INTO site_expenses (project_id, name, amount, currency, spent_on, added_at, added_by_id) VALUES (@p, 'Concrete', @a, 'USD', UTC_DATE(), UTC_TIMESTAMP(3), @u)",
            ("@p", siteId), ("@a", amount), ("@u", userId));

    /// <summary>An Execution admin the super admin granted exactly <paramref name="codes"/>, on top of an admin's defaults.</summary>
    private async Task<AuthResponse> AdminWithAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        if (codes.Length > 0)
        {
            var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
                new { changes = codes.Select(code => new { code, action = "grant", reason = "BOQ work" }) }, superAdmin.AccessToken);
            granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        }

        return admin;
    }

    [Fact]
    public async Task A_BOQ_is_started_on_a_card_and_nothing_exists_until_Confirm()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);

        var (create, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "create", project_id = site.Id, kind = "client", title = "Villa Mounir" });
        var card = create.PendingAction!;
        card.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        card.Preview.Changes.Select(c => (c.Label, c.After)).ShouldBe([("Kind", "Client BOQ"), ("Title", "Villa Mounir")]);
        (await ListAsync(superAdmin, site.Id)).Client.ShouldBeEmpty();

        await ConfirmAsync(superAdmin, create);
        var made = (await ListAsync(superAdmin, site.Id)).Client.ShouldHaveSingleItem();
        made.ShouldSatisfyAllConditions(b => b.Title.ShouldBe("Villa Mounir"), b => b.Status.ShouldBe(BoqStatus.Draft), b => b.Number.ShouldNotBeNull());

        // a variation order needs the client BOQ agreed first, and a reason; both are said before any card
        var (_, early) = await ProposeAsync(superAdmin, "propose_boq", new { step = "create", project_id = site.Id, kind = "vo", reason = "A garage" });
        Message(early).ShouldContain("needs the client BOQ to be agreed first");

        // an internal draft starts from the lines of another BOQ of the site
        var villa = await VillaAsync(superAdmin, site.Id);
        var (draft, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "create", project_id = site.Id, kind = "internal", from_boq_id = villa.Id });
        draft.PendingAction!.Preview.Changes.Single(c => c.Label == "Starts with").After.ShouldContain("2 line(s)");
        draft.PendingAction.Preview.Changes.Single(c => c.Label == "Starts with").After.ShouldContain("USD 1,200");
        await ConfirmAsync(superAdmin, draft);
        var internalDraft = (await ListAsync(superAdmin, site.Id)).Internal.ShouldHaveSingleItem();
        (await GetBoqAsync(superAdmin, internalDraft.Id)).Lines.Select(l => l.Description).ShouldBe(["Slab", "Walls"]);
        internalDraft.Number.ShouldBeNull();
    }

    [Fact]
    public async Task A_BOQ_is_duplicated_and_lines_are_copied_into_a_draft_with_the_new_total_typed_for_a_client_BOQ()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);

        var (duplicate, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "duplicate", boq_id = villa.Id });
        duplicate.PendingAction!.Preview.Changes.Single(c => c.Label == "New draft").After.ShouldBe("Villa Mounir (copy)");
        await ConfirmAsync(superAdmin, duplicate);
        var copy = (await ListAsync(superAdmin, site.Id)).Client.Single(b => b.Title == "Villa Mounir (copy)");
        (copy.TotalUsd, copy.Status).ShouldBe((1200m, BoqStatus.Draft));

        // lines of the villa go into an empty client draft: the card lists them and the user types the total the draft reaches
        var target = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Option B");
        var (copyLines, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "copy_lines", boq_id = target.Id, from_boq_id = villa.Id, from_lines = new[] { 1 } });
        var card = copyLines.PendingAction!;
        card.Preview.Changes.Select(c => c.Label).ShouldBe(["Copied line", "BOQ total"]);
        card.Preview.Changes[0].After.ShouldContain("Slab: 10 m3 × 100 = USD 1,000");
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, "1000"));
        await (await TryConfirmAsync(superAdmin, copyLines, "1200")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, copyLines, "1000");
        (await GetBoqAsync(superAdmin, target.Id)).ShouldSatisfyAllConditions(b => b.TotalUsd.ShouldBe(1000m), b => b.Lines.Single().Description.ShouldBe("Slab"));

        // a line that isn't in the source is named, not guessed
        var (_, missing) = await ProposeAsync(superAdmin, "propose_boq", new { step = "copy_lines", boq_id = target.Id, from_boq_id = villa.Id, from_lines = new[] { 7 } });
        Message(missing).ShouldContain("has 2 line(s)");

        // an internal draft takes lines without the total typed: it is a private working paper
        var working = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "internal", "Working paper");
        var (toDraft, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "copy_lines", boq_id = working.Id, from_boq_id = villa.Id });
        toDraft.PendingAction!.Preview.ShouldSatisfyAllConditions(p => p.Confirm.ShouldBe(ProtonConfirm.TwoStep), p => p.Phrase.ShouldBeNull());
        await ConfirmAsync(superAdmin, toDraft);
        (await GetBoqAsync(superAdmin, working.Id)).Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_draft_is_deleted_with_its_title_typed_and_an_agreed_BOQ_is_not()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);
        var spare = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Spare");

        var (delete, _) = await ProposeAsync(superAdmin, "propose_boq", new { step = "delete", boq_id = spare.Id });
        var card = delete.PendingAction!;
        (card.Preview.Confirm, card.Preview.Phrase, card.Preview.Irreversible).ShouldBe((ProtonConfirm.Typed, "Spare", true));
        await (await TryConfirmAsync(superAdmin, delete, "spare?")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        (await Client.GetAsync($"/api/boqs/{spare.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ConfirmAsync(superAdmin, delete, "Spare");
        (await Client.GetAsync($"/api/boqs/{spare.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Boqs.AgreeAsync(superAdmin.AccessToken, villa.Id);
        var (_, agreed) = await ProposeAsync(superAdmin, "propose_boq", new { step = "delete", boq_id = villa.Id });
        Message(agreed).ShouldContain("agreed");
        var (_, copied) = await ProposeAsync(superAdmin, "propose_boq", new { step = "copy_lines", boq_id = villa.Id, from_boq_id = spare.Id });
        Message(copied).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Editing_a_BOQ_lists_each_line_and_the_total_and_only_rates_the_user_typed_reach_a_card()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);
        const string said = "Make the walls 60 a square metre, drop the slab and add paint, 10 m2 at 15";

        var (edit, _) = await ProposeAsync(superAdmin, "propose_edit_boq", new
        {
            boq_id = villa.Id,
            change_lines = new[] { new { line = 2, rate = 60m } },
            remove_lines = new[] { 1 },
            add_lines = new[] { new { description = "Paint", quantity = 10m, unit = "m2", rate = 15m } },
        }, said);
        var card = edit.PendingAction!;
        card.Preview.Changes.Select(c => c.Label).ShouldBe(["Line 1", "Line 2", "New line", "BOQ total", "Client pays (supervision and TVA included)"]);
        card.Preview.Changes[0].ShouldSatisfyAllConditions(c => c.Before!.ShouldContain("Slab"), c => c.After.ShouldBe("removed"));
        card.Preview.Changes[1].ShouldSatisfyAllConditions(c => c.Before!.ShouldContain("4 m2 × 50 = USD 200"), c => c.After.ShouldContain("4 m2 × 60 = USD 240"));
        card.Preview.Changes[2].After.ShouldContain("Paint: 10 m2 × 15 = USD 150");
        card.Preview.Changes[3].ShouldSatisfyAllConditions(c => c.Before.ShouldBe("USD 1,200"), c => c.After.ShouldBe("USD 390"));
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, "390"));
        (await GetBoqAsync(superAdmin, villa.Id)).TotalUsd.ShouldBe(1200m);

        await (await TryConfirmAsync(superAdmin, edit, "1200")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, edit, "390");
        var saved = await GetBoqAsync(superAdmin, villa.Id);
        saved.TotalUsd.ShouldBe(390m);
        saved.Lines.Select(l => (l.Description, l.Rate)).ShouldBe([("Walls", 60m), ("Paint", 15m)]);

        // a rate the user didn't type never reaches a card
        var (_, guessed) = await ProposeAsync(superAdmin, "propose_edit_boq",
            new { boq_id = villa.Id, change_lines = new[] { new { line = 1, rate = 75m } } }, "Raise the walls a bit");
        Message(guessed).ShouldContain("didn't give 75");

        // a line kept as it was needn't be typed again; a change of words moves no money, so no total is typed
        var (retitle, _) = await ProposeAsync(superAdmin, "propose_edit_boq", new { boq_id = villa.Id, title = "Villa Mounir, phase 1", change_lines = new[] { new { line = 1, code = "A" } } });
        (retitle.PendingAction!.Preview.Confirm, retitle.PendingAction.Preview.Phrase).ShouldBe((ProtonConfirm.TwoStep, null));
        await ConfirmAsync(superAdmin, retitle);
        (await GetBoqAsync(superAdmin, villa.Id)).ShouldSatisfyAllConditions(b => b.Title.ShouldBe("Villa Mounir, phase 1"), b => b.Lines[0].Code.ShouldBe("A"));

        // a discount typed as a percentage becomes the fraction the app stores
        var (discount, _) = await ProposeAsync(superAdmin, "propose_edit_boq", new { boq_id = villa.Id, adjustment = "percent", adjustment_value = -10m }, "Give them 10% off");
        discount.PendingAction!.Preview.Changes.Single(c => c.Label == "Adjustment").After.ShouldBe("-10% of the lines");
        await ConfirmAsync(superAdmin, discount, "351");
        (await GetBoqAsync(superAdmin, villa.Id)).ShouldSatisfyAllConditions(b => b.AdjustmentType.ShouldBe(BoqAdjustmentType.Percent), b => b.AdjustmentValue.ShouldBe(-0.10m), b => b.TotalUsd.ShouldBe(351m));

        // a line that isn't there, or nothing at all to change, are said before any card
        var (_, noLine) = await ProposeAsync(superAdmin, "propose_edit_boq", new { boq_id = villa.Id, remove_lines = new[] { 9 } });
        Message(noLine).ShouldContain("has 2 line(s)");
        var (_, nothing) = await ProposeAsync(superAdmin, "propose_edit_boq", new { boq_id = villa.Id });
        Message(nothing).ShouldContain("Nothing would change");
    }

    [Fact]
    public async Task A_card_for_a_BOQ_saved_by_someone_since_is_refused_and_an_agreed_BOQ_cannot_be_edited()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);

        var (edit, _) = await ProposeAsync(superAdmin, "propose_edit_boq",
            new { boq_id = villa.Id, add_lines = new[] { new { description = "Paint", quantity = 10m, rate = 15m } } }, "Add paint, 10 at 15");
        await Boqs.SaveAsync(superAdmin.AccessToken, villa.Id, [Line("Slab", 10, 100m)], "Villa Mounir");
        await (await TryConfirmAsync(superAdmin, edit, "1350")).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        (await GetBoqAsync(superAdmin, villa.Id)).Lines.Select(l => l.Description).ShouldBe(["Slab"]);

        await Boqs.AgreeAsync(superAdmin.AccessToken, villa.Id);
        var (_, agreed) = await ProposeAsync(superAdmin, "propose_edit_boq", new { boq_id = villa.Id, title = "Other" });
        Message(agreed).ShouldContain("agreed");
    }

    [Fact]
    public async Task Agreeing_sets_the_work_price_with_the_total_typed_and_withdrawing_needs_a_reason_and_leaves_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Works as agreed", 1, 1000m, unit: "lot")]);

        var (agree, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "agree" });
        var card = agree.PendingAction!;
        card.Preview.Changes.Select(c => (c.Label, c.Before, c.After)).ShouldBe(
        [
            ("BOQ", "Draft", "Agreed"),
            ("Work price", "USD 0", "USD 1,000"),
            ("Supervision (10%)", "USD 0", "USD 100"),
            ("TVA (11%, on the work only)", "USD 0", "USD 110"),
            ("The client pays", "USD 0", "USD 1,210"),
        ]);
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, "1000"));
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(0m);
        await (await TryConfirmAsync(superAdmin, agree, "1,210")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, agree, "1000");
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(1000m);
        (await GetBoqAsync(superAdmin, client.Id)).Status.ShouldBe(BoqStatus.Agreed);

        // a variation order is agreed on top of it
        var vo = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "vo", reason: "A garage");
        await Boqs.SaveAsync(superAdmin.AccessToken, vo.Id, [Line("Garage", 1, 300m, unit: "lot")], "Variation order", reason: "A garage");
        var (agreeVo, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = vo.Id, step = "agree" });
        agreeVo.PendingAction!.Preview.Changes.Single(c => c.Label == "Work price").ShouldSatisfyAllConditions(c => c.Before.ShouldBe("USD 1,000"), c => c.After.ShouldBe("USD 1,300"));
        await ConfirmAsync(superAdmin, agreeVo, "300");
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(1300m);

        // the client BOQ stays while a variation order stands on it; a withdrawal needs a reason
        var (_, held) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "withdraw", reason = "Starting over" });
        Message(held).ShouldContain("Variation orders are agreed on top of this BOQ");
        var (_, noReason) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = vo.Id, step = "withdraw" });
        Message(noReason).ShouldContain("needs a reason");

        var (withdraw, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = vo.Id, step = "withdraw", reason = "The garage was dropped" });
        withdraw.PendingAction!.Preview.Changes.Select(c => c.Label).ShouldContain("Reason");
        withdraw.PendingAction.Preview.Note!.ShouldContain("returns to draft");
        (withdraw.PendingAction.Preview.Confirm, withdraw.PendingAction.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, "300"));
        await ConfirmAsync(superAdmin, withdraw, "300");
        (await GetBoqAsync(superAdmin, vo.Id)).Status.ShouldBe(BoqStatus.Draft);
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(1000m);
        var history = await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken));
        history[0].Description.ShouldBe("The garage was dropped");

        // a draft has nothing to withdraw, and an agreed one nothing to agree
        var (_, notAgreed) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = vo.Id, step = "withdraw", reason = "Again" });
        Message(notAgreed).ShouldContain("isn't agreed");
        var (_, again) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "agree" });
        Message(again).ShouldContain("already agreed");
    }

    [Fact]
    public async Task A_permit_line_is_charged_with_no_supervision_or_TVA_on_the_cards()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Works as agreed", 1, 1000m, unit: "lot")]);

        // a permit added on the card: the work stays, the permits are apart, and the client pays them as they are
        var (edit, _) = await ProposeAsync(superAdmin, "propose_edit_boq",
            new { boq_id = client.Id, add_lines = new[] { new { description = "Building permit", quantity = 1m, rate = 200m, permit = true } } },
            "Add the building permit at 200");
        var card = edit.PendingAction!;
        card.Preview.Changes.Single(c => c.Label == "New line").After.ShouldStartWith("Permit: Building permit");
        card.Preview.Changes.Single(c => c.Label == "Work total").ShouldSatisfyAllConditions(c => c.Before.ShouldBe("USD 1,000"), c => c.After.ShouldBe("USD 1,000"));
        card.Preview.Changes.Single(c => c.Label == "Permits (no supervision or TVA)").After.ShouldBe("USD 200");
        // 1,000 of work with 10% supervision and 11% TVA, and 200 of permits with neither
        card.Preview.Changes.Single(c => c.Label.StartsWith("Client pays")).After.ShouldBe("USD 1,410");
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, "1200"));
        await ConfirmAsync(superAdmin, edit, "1200");
        var saved = await GetBoqAsync(superAdmin, client.Id);
        (saved.TotalUsd, saved.PermitsUsd).ShouldBe((1000m, 200m));
        saved.Lines.Last().Nature.ShouldBe(BoqLineNature.Permit);

        // agreeing sets the work price and the permits; supervision and TVA stay on the work
        var (agree, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "agree" });
        agree.PendingAction!.Preview.Changes.Select(c => (c.Label, c.After)).ShouldBe(
        [
            ("BOQ", "Agreed"),
            ("Work price", "USD 1,000"),
            ("Permits (no supervision or TVA)", "USD 200"),
            ("Supervision (10%)", "USD 100"),
            ("TVA (11%, on the work only)", "USD 110"),
            ("The client pays", "USD 1,410"),
        ]);
        await ConfirmAsync(superAdmin, agree, "1200");
        var money = (await ProjectAsync(superAdmin, site.Id)).Money!;
        (money.ProjectFee, money.PermitsUsd).ShouldBe((1000m, 200m));
    }

    [Fact]
    public async Task A_withdrawal_that_would_take_the_profit_to_zero_is_refused_at_Confirm_and_kept_for_forcing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);
        await CostAsync(site.Id, superAdmin.User.Id, 600m);

        var (withdraw, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "withdraw", reason = "Starting over" });
        withdraw.PendingAction!.Preview.Note!.ShouldContain("it is refused");
        var refused = await (await TryConfirmAsync(superAdmin, withdraw, "1000")).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        refused.BlockedChangeId.ShouldNotBeNull();
        (await GetBoqAsync(superAdmin, client.Id)).Status.ShouldBe(BoqStatus.Agreed);
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_card_for_an_agreement_is_refused_once_the_price_has_moved()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var client = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id);
        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Works", 1, 1000m)]);
        var (agree, _) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = client.Id, step = "agree" });

        await Boqs.SaveAsync(superAdmin.AccessToken, client.Id, [Line("Works", 1, 2000m)]);
        await (await TryConfirmAsync(superAdmin, agree, "1000")).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        (await GetBoqAsync(superAdmin, client.Id)).Status.ShouldBe(BoqStatus.Draft);
    }

    [Fact]
    public async Task A_send_by_WhatsApp_is_noted_on_the_BOQ_and_an_email_goes_through_the_email_tool()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);

        var (note, _) = await ProposeAsync(superAdmin, "propose_boq_agreement",
            new { boq_id = villa.Id, step = "record_delivery", channel = "whatsapp", recipient = "Mr Nabil" });
        note.PendingAction!.Preview.Changes.Select(c => (c.Label, c.After)).ShouldBe([("How", "WhatsApp"), ("Total when sent", "USD 1,200"), ("To", "Mr Nabil")]);
        note.PendingAction.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        await ConfirmAsync(superAdmin, note);
        (await GetBoqAsync(superAdmin, villa.Id)).Deliveries.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            d => d.Channel.ShouldBe(DeliveryChannel.Whatsapp), d => d.Recipient.ShouldBe("Mr Nabil"), d => d.TotalUsd.ShouldBe(1200m));

        var (_, email) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = villa.Id, step = "record_delivery", channel = "email" });
        Message(email).ShouldContain("propose_send_boq");
        var internalDraft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "internal", "Working paper");
        await Boqs.SaveAsync(superAdmin.AccessToken, internalDraft.Id, [Line("Slab", 1, 10m)], "Working paper");
        var (_, nothingToSend) = await ProposeAsync(superAdmin, "propose_boq_agreement", new { boq_id = internalDraft.Id, step = "record_delivery", channel = "whatsapp" });
        Message(nothingToSend).ShouldContain("never sent to the client");
    }

    [Fact]
    public async Task A_BOQ_is_emailed_only_to_an_address_the_user_typed_with_the_address_typed_to_confirm()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);
        var to = AuthTestSteps.NewEmail("client-boq");

        // an address the user didn't type, however it got to the model, never reaches a card
        var (_, guessed) = await ProposeAsync(superAdmin, "propose_send_boq", new { boq_id = villa.Id, to }, "Send the Villa BOQ to the client");
        Message(guessed).ShouldContain("only when the user typed it");

        var (send, _) = await ProposeAsync(superAdmin, "propose_send_boq", new { boq_id = villa.Id, to, note = "Sent after the call" }, $"Email the Villa BOQ to {to}");
        var card = send.PendingAction!;
        card.Preview.ShouldSatisfyAllConditions(
            p => p.Confirm.ShouldBe(ProtonConfirm.Typed),
            p => p.Phrase.ShouldBe(to),
            p => p.Irreversible.ShouldBeTrue(),
            p => p.Reach!.ShouldContain("outside the company"),
            p => p.Note!.ShouldContain("still a draft"));
        card.Preview.Changes.Select(c => c.Label).ShouldBe(["To", "Sends", "Greeting", "Total the client reads", "Note kept with the delivery"]);
        card.Preview.Changes.Single(c => c.Label == "Total the client reads").After.ShouldContain("USD 1,452");
        api.Emails.CountTo(to).ShouldBe(0);

        await (await TryConfirmAsync(superAdmin, send, "someone.else@test.local")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        api.Emails.CountTo(to).ShouldBe(0);
        await ConfirmAsync(superAdmin, send, to);
        api.Emails.CountTo(to).ShouldBe(1);
        var email = api.Emails.LastTo(to);
        email.Attachments!.Single().FileName.ShouldBe(villa.Number!.ToLowerInvariant() + ".pdf");
        var delivery = (await GetBoqAsync(superAdmin, villa.Id)).Deliveries.ShouldHaveSingleItem();
        (delivery.Channel, delivery.Recipient, delivery.Note).ShouldBe((DeliveryChannel.Email, to, "Sent after the call"));

        // the confirmed card runs once
        await (await TryConfirmAsync(superAdmin, send, to)).ShouldFailWithAsync(409, ErrorCodes.ProtonActionNotPending);
        api.Emails.CountTo(to).ShouldBe(1);
    }

    [Fact]
    public async Task An_email_card_is_refused_for_a_draft_that_never_goes_to_the_client_a_BOQ_changed_since_and_a_bad_address()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);
        var internalDraft = await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, "internal", "Working paper");
        await Boqs.SaveAsync(superAdmin.AccessToken, internalDraft.Id, [Line("Slab", 1, 10m)], "Working paper");
        var to = AuthTestSteps.NewEmail("client-boq");

        var (_, internalOnly) = await ProposeAsync(superAdmin, "propose_send_boq", new { boq_id = internalDraft.Id, to }, $"Email the working paper to {to}");
        Message(internalOnly).ShouldContain("never sent to the client");
        var (_, empty) = await ProposeAsync(superAdmin, "propose_send_boq",
            new { boq_id = (await Boqs.CreateAsync(superAdmin.AccessToken, site.Id, title: "Empty")).Id, to }, $"Email the empty one to {to}");
        Message(empty).ShouldContain("no lines");
        var (_, badAddress) = await ProposeAsync(superAdmin, "propose_send_boq", new { boq_id = villa.Id, to = "not an email" }, "Email it to not an email");
        badAddress["status"]!.GetValue<string>().ShouldBe("invalid");

        // saved after the card: the email would say something else than the card did
        var (send, _) = await ProposeAsync(superAdmin, "propose_send_boq", new { boq_id = villa.Id, to }, $"Email the Villa BOQ to {to}");
        await Boqs.SaveAsync(superAdmin.AccessToken, villa.Id, [Line("Slab", 10, 100m)], "Villa Mounir");
        await (await TryConfirmAsync(superAdmin, send, to)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        api.Emails.CountTo(to).ShouldBe(0);
    }

    [Fact]
    public async Task Who_may_use_the_BOQ_cards_follows_the_permissions_of_the_endpoints()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        var villa = await VillaAsync(superAdmin, site.Id);
        var drafter = await AdminWithAsync(superAdmin, PermissionCodes.BoqCreate);
        var manager = await AdminWithAsync(superAdmin, PermissionCodes.ProjectPricingManage);
        var plain = await AdminWithAsync(superAdmin);
        var to = AuthTestSteps.NewEmail("client-boq");

        // boq:create alone writes internal drafts, not the client's BOQ, and is offered nothing that agrees, sends or prices
        var (internalCard, _) = await ProposeAsync(drafter, "propose_boq", new { step = "create", project_id = site.Id, kind = "internal" });
        internalCard.PendingAction.ShouldNotBeNull();
        var (_, clientRefused) = await ProposeAsync(drafter, "propose_boq", new { step = "create", project_id = site.Id, kind = "client" });
        Message(clientRefused).ShouldContain("manage pricing");
        foreach (var (tool, input) in new (string, object)[]
        {
            ("propose_boq_agreement", new { boq_id = villa.Id, step = "agree" }),
            ("propose_send_boq", new { boq_id = villa.Id, to }),
            ("propose_site_pricing", new { project_id = site.Id, supervision_percent = 12m, reason = "More supervision" }),
        })
        {
            var (_, unavailable) = await ProposeAsync(drafter, tool, input, $"Do it for {to}, 12");
            Message(unavailable).ShouldContain("isn't available to this user");
        }

        // an admin with neither is offered none of them
        var (_, none) = await ProposeAsync(plain, "propose_boq", new { step = "create", project_id = site.Id, kind = "internal" });
        Message(none).ShouldContain("isn't available to this user");

        // pricing_manage alone does everything on the client's BOQ
        var (agree, _) = await ProposeAsync(manager, "propose_boq_agreement", new { boq_id = villa.Id, step = "agree" });
        await ConfirmAsync(manager, agree, "1200");
        (await ProjectAsync(superAdmin, site.Id)).Money!.ProjectFee.ShouldBe(1200m);

        // permissions taken away after the card are checked again at Confirm
        var (duplicate, _) = await ProposeAsync(manager, "propose_boq", new { step = "duplicate", boq_id = villa.Id });
        var revoked = await Client.PutJsonAsync($"/api/users/{manager.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ProjectPricingManage, action = "revoke", reason = "No longer" } } }, superAdmin.AccessToken);
        revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync());
        (await TryConfirmAsync(manager, duplicate)).IsSuccessStatusCode.ShouldBeFalse();
        (await ListAsync(superAdmin, site.Id)).Client.Count(b => b.Title == "Villa Mounir (copy)").ShouldBe(0);
    }

    [Fact]
    public async Task A_sites_supervision_and_TVA_change_with_a_reason_and_only_rates_the_user_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);

        var (pricing, _) = await ProposeAsync(superAdmin, "propose_site_pricing",
            new { project_id = site.Id, supervision_percent = 12.5m, reason = "The client wants more supervision" }, "Set the supervision to 12.5%");
        var card = pricing.PendingAction!;
        card.Preview.Changes.Select(c => (c.Label, c.Before, c.After)).ShouldBe(
        [
            ("Supervision rate", "10%", "12.5%"),
            ("Supervision", "USD 100", "USD 125"),
            ("The client pays", "USD 1,210", "USD 1,235"),
            ("Why", null, "The client wants more supervision"),
        ]);
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, site.Title));
        (await ProjectAsync(superAdmin, site.Id)).Money!.SupervisionRate.ShouldBe(0.10m);

        await (await TryConfirmAsync(superAdmin, pricing, "12.5")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, pricing, site.Title);
        (await ProjectAsync(superAdmin, site.Id)).Money.ShouldSatisfyAllConditions(m => m!.SupervisionRate.ShouldBe(0.125m), m => m!.TvaRate.ShouldBe(0.11m), m => m!.ProjectFee.ShouldBe(1000m));
        (await OkAsync<List<ProjectPaymentChangeResponse>>(Client.GetAsync($"/api/projects/{site.Id}/payment-changes", superAdmin.AccessToken)))[0]
            .Description.ShouldBe("The client wants more supervision");

        // TVA alone
        var (tva, _) = await ProposeAsync(superAdmin, "propose_site_pricing",
            new { project_id = site.Id, tva_percent = 0m, reason = "The client is exempt" }, "No TVA for them: 0%");
        tva.PendingAction!.Preview.Changes.Select(c => c.Label).ShouldBe(["TVA rate", "TVA (on the work only)", "The client pays", "Why"]);

        // rates the user didn't type, no reason, nothing different, and a Design project are all said before any card
        var (_, guessed) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, supervision_percent = 15m, reason = "Why not" }, "Raise the supervision a bit");
        Message(guessed).ShouldContain("didn't give 15");
        var (_, noReason) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, supervision_percent = 15m, reason = "  " }, "Set supervision to 15%");
        Message(noReason).ShouldContain("why the rates change");
        var (_, same) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, supervision_percent = 12.5m, reason = "Same" }, "Set supervision to 12.5%");
        Message(same).ShouldContain("Nothing would change");
        var (_, neither) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, reason = "Because" });
        Message(neither).ShouldContain("Say which rate changes");
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var (_, wrongDomain) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = design.Id, supervision_percent = 15m, reason = "Why" }, "Set supervision to 15%");
        Message(wrongDomain).ShouldContain("Only an Execution site");
    }

    [Fact]
    public async Task A_pricing_card_is_refused_once_the_rates_changed_and_a_lower_supervision_is_held_to_the_profit_floor()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await NewSiteAsync(superAdmin);
        await Boqs.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);

        var (stale, _) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, supervision_percent = 12m, reason = "More" }, "Supervision to 12%");
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { supervisionRate = 0.09m, description = "Someone else" }, superAdmin.AccessToken));
        await (await TryConfirmAsync(superAdmin, stale, site.Title)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        (await ProjectAsync(superAdmin, site.Id)).Money!.SupervisionRate.ShouldBe(0.09m);

        // revenue is 1,090 (work and 9% supervision) against a cost of 1,000: taking the supervision off leaves less than nothing
        await CostAsync(site.Id, superAdmin.User.Id, 1000m);
        var (lower, _) = await ProposeAsync(superAdmin, "propose_site_pricing", new { project_id = site.Id, supervision_percent = 0m, reason = "Free supervision" }, "No supervision at all: 0%");
        lower.PendingAction!.Preview.Note!.ShouldContain("it is refused");
        var refused = await (await TryConfirmAsync(superAdmin, lower, site.Title)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        refused.BlockedChangeId.ShouldNotBeNull();
        (await ProjectAsync(superAdmin, site.Id)).Money!.SupervisionRate.ShouldBe(0.09m);
    }

    [Fact]
    public async Task A_Design_projects_billing_is_changed_with_what_it_drops_on_the_card_and_its_name_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var newClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var first = project.Client!.DisplayName;

        // another client: the previous client's portal access ends
        var (move, _) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = newClient.Id });
        var card = move.PendingAction!;
        card.Preview.Changes.Select(c => (c.Label, c.Before, c.After)).ShouldBe([("Client", first, newClient.DisplayName)]);
        card.Preview.Note!.ShouldContain("lose their access");
        (card.Preview.Confirm, card.Preview.Phrase, card.Preview.Irreversible).ShouldBe((ProtonConfirm.Typed, project.Title, true));
        await (await TryConfirmAsync(superAdmin, move, "Another")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, move, project.Title);
        (await ProjectAsync(superAdmin, project.Id)).Client!.Id.ShouldBe(newClient.Id);

        // internal work has no client and no payment
        var (internalCard, _) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "internal" });
        internalCard.PendingAction!.Preview.Changes.Select(c => (c.Label, c.Before, c.After)).ShouldBe(
        [
            ("Billing", "Client work", "Internal work"),
            ("Client", newClient.DisplayName, "none"),
            ("Agreed revenue", "USD 5,000", "USD 0"),
            ("TVA", "0%", "0%"),
        ]);
        await ConfirmAsync(superAdmin, internalCard, project.Title);
        (await ProjectAsync(superAdmin, project.Id)).ShouldSatisfyAllConditions(
            p => p.BillingType.ShouldBe(BillingType.Internal), p => p.Client.ShouldBeNull(), p => p.Money!.Revenue.ShouldBe(0m));

        // and back to client work, which has no revenue until it is set
        var (back, _) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = newClient.Id });
        back.PendingAction!.Preview.Note!.ShouldContain("stays at 0 until it is set");
        await ConfirmAsync(superAdmin, back, project.Title);
        (await ProjectAsync(superAdmin, project.Id)).BillingType.ShouldBe(BillingType.Client);
    }

    [Fact]
    public async Task A_billing_card_is_refused_for_a_site_a_client_with_nothing_to_choose_and_a_project_with_payments()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var other = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await NewSiteAsync(superAdmin);

        var (_, execution) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = site.Id, billing_type = "internal" });
        Message(execution).ShouldContain("always client work");
        var (_, noClient) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = (long?)null });
        Message(noClient).ShouldContain("Choose the client");
        var (_, internalWithClient) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "internal", client_id = other.Id });
        Message(internalWithClient).ShouldContain("Internal projects have no client");
        var executionClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (_, wrongDomain) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = executionClient.Id });
        Message(wrongDomain).ShouldContain("does not exist in this domain");

        // a client changed under the card: it would put the old billing back over theirs
        var (stale, _) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = other.Id });
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{project.Id}/billing",
            new { billingType = "client", clientId = (await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design)).Id }, superAdmin.AccessToken));
        await (await TryConfirmAsync(superAdmin, stale, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        // invoices and payments keep the client they were made for
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 100m, currency = "USD" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var (_, billed) = await ProposeAsync(superAdmin, "propose_project_billing", new { project_id = project.Id, billing_type = "client", client_id = other.Id });
        Message(billed).ShouldContain("already has invoices or payments");
    }
}
