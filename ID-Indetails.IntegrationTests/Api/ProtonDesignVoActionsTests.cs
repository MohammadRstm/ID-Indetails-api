using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A Design project's variation orders, read and proposed by Proton with a scripted model: every card is strong and typed with
/// the project's name, the amounts are the user's own words, the VO is made, confirmed or cancelled through the VO service only
/// on Confirm, a profit-floor refusal is kept as a blocked change, and the email reaches only an address the user typed. Every
/// test follows its own project, since the classes share one database.
/// </summary>
public class ProtonDesignVoActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

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

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private static string Message(JsonNode result) => result["message"]!.GetValue<string>();

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    /// <summary>A Design client project of <paramref name="price"/> before 11% TVA, with the Design department.</summary>
    private async Task<ProjectResponse> ProjectAsync(AuthResponse superAdmin, decimal price = 10000m)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = price,
            tvaRate = 0.11m,
            departmentIds = new[] { await _steps.DesignDepartmentAsync() },
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<decimal> PriceAsync(AuthResponse who, long projectId)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", who.AccessToken))).Money!.Revenue;

    private Task<List<DesignVoResponse>> VosAsync(AuthResponse who, long projectId)
        => OkAsync<List<DesignVoResponse>>(Client.GetAsync($"/api/projects/{projectId}/design-vos", who.AccessToken));

    private async Task<DesignVoResponse> MakeAsync(AuthResponse who, long projectId, decimal amount, bool apply = false)
        => await OkAsync<DesignVoResponse>(Client.PostJsonAsync($"/api/projects/{projectId}/design-vos",
            new { title = "Terrace", lines = new[] { new { description = "Terrace", amount } }, apply }, who.AccessToken));

    /// <summary>A Design admin granted exactly <paramref name="codes"/> on top of an admin's defaults.</summary>
    private async Task<AuthResponse> AdminWithAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        if (codes.Length > 0)
        {
            var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
                new { changes = codes.Select(code => new { code, action = "grant", reason = "VO work" }) }, superAdmin.AccessToken);
            granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        }

        return admin;
    }

    [Fact]
    public async Task The_VOs_are_read_with_the_price_and_the_departments_only_by_whoever_sees_the_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 10000m);
        var applied = await MakeAsync(superAdmin, project.Id, 2000m, apply: true);
        var pending = await MakeAsync(superAdmin, project.Id, -500m);

        var (_, read) = await ProposeAsync(superAdmin, "list_design_vos", new { project_id = project.Id }, "What VOs does the project have?");
        var data = read["data"]!;
        data["price_usd"]!.GetValue<decimal>().ShouldBe(12000m);
        (data["applied_usd"]!.GetValue<decimal>(), data["pending_usd"]!.GetValue<decimal>()).ShouldBe((2000m, -500m));
        data["outside_scope_minutes"]!.GetValue<int>().ShouldBe(0);
        data["departments"]!.AsArray().ShouldHaveSingleItem()!["department_id"]!.GetValue<long>().ShouldBe(await _steps.DesignDepartmentAsync());
        var rows = data["vos"]!.AsArray();
        rows.Select(v => v!["id"]!.GetValue<long>()).ShouldBe([pending.Id, applied.Id]);
        (rows[0]!["status"]!.GetValue<string>(), rows[0]!["total_with_tva_usd"]!.GetValue<decimal>()).ShouldBe(("pending", -555m));
        rows[1]!["lines"]!.AsArray().ShouldHaveSingleItem()!["amount"]!.GetValue<decimal>().ShouldBe(2000m);

        // an admin without financial:view isn't offered the tool at all
        var plain = await AdminWithAsync(superAdmin);
        var (_, refused) = await ProposeAsync(plain, "list_design_vos", new { project_id = project.Id }, "What VOs does the project have?");
        Message(refused).ShouldContain("isn't available to this user");
    }

    [Fact]
    public async Task A_VO_is_made_and_applied_on_a_card_with_every_amount_typed_and_nothing_exists_until_Confirm()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 10000m);
        var input = new
        {
            step = "create", project_id = project.Id, title = "Guest house", apply = true,
            lines = new[] { new { description = "Guest house design", amount = 9000m }, new { description = "No pergola", amount = -1000m } },
        };

        // an amount the user didn't give, however the model came by it, never reaches a card
        var (_, untyped) = await ProposeAsync(superAdmin, "propose_design_vo", input, "Add a guest house VO and apply it");
        Message(untyped).ShouldContain("a money card only uses amounts the user typed");

        var (create, _) = await ProposeAsync(superAdmin, "propose_design_vo", input, "Apply a Guest house VO: 9,000 for the design, 1,000 off for no pergola");
        var card = create.PendingAction!;
        card.Preview.ShouldSatisfyAllConditions(
            p => p.Confirm.ShouldBe(ProtonConfirm.Typed),
            p => p.Phrase.ShouldBe(project.Title),
            p => p.Note!.ShouldContain("joins the price at once"));
        card.Preview.Changes.Single(c => c.Label == "Total (before TVA)").After.ShouldBe("USD 8,000");
        card.Preview.Changes.Single(c => c.Label == "Total with TVA").After.ShouldBe("USD 8,880");
        var price = card.Preview.Changes.Single(c => c.Label == "The price becomes");
        price.Before.ShouldStartWith("USD 10,000");
        price.After.ShouldStartWith("USD 18,000");
        card.Preview.Changes.Single(c => c.Label.EndsWith(" gets")).After.ShouldStartWith("USD 8,000");
        (await VosAsync(superAdmin, project.Id)).ShouldBeEmpty();

        // the project's name is typed, not the amount
        await (await TryConfirmAsync(superAdmin, create, "8000")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        (await VosAsync(superAdmin, project.Id)).ShouldBeEmpty();

        var done = await ConfirmAsync(superAdmin, create, project.Title);
        done.Message.ShouldContain("Applied");
        var vo = (await VosAsync(superAdmin, project.Id)).ShouldHaveSingleItem();
        (vo.Status, vo.TotalUsd, vo.Lines.Count).ShouldBe((DesignVoStatus.Applied, 8000m, 2));
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(18000m);

        // a card for the user's own split: the departments must be the project's, adding up to the total
        var department = await _steps.DesignDepartmentAsync();
        var (_, wrongSum) = await ProposeAsync(superAdmin, "propose_design_vo", new
        {
            step = "create", project_id = project.Id, title = "Soil study", split = "departments",
            lines = new[] { new { description = "Soil study", amount = 2000m } }, allocations = new[] { new { department_id = department, amount = 1500m } },
        }, "A soil study VO of 2,000, 1,500 to Architecture");
        Message(wrongSum).ShouldContain("add up");
    }

    [Fact]
    public async Task A_pending_VO_is_confirmed_into_the_price_and_another_is_cancelled_and_a_stale_card_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 10000m);
        var terrace = await MakeAsync(superAdmin, project.Id, 1500m);
        var pool = await MakeAsync(superAdmin, project.Id, 5000m);

        var (confirm, _) = await ProposeAsync(superAdmin, "propose_design_vo", new { step = "confirm", vo_id = terrace.Id });
        var card = confirm.PendingAction!;
        (card.Preview.Confirm, card.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, project.Title));
        card.Preview.Changes.Single(c => c.Label == "The price becomes").After.ShouldStartWith("USD 11,500");
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(10000m);

        // the price moved after the card: what the user read is no longer what would be made
        await OkAsync<DesignVoResponse>(Client.PostEmptyAsync($"/api/design-vos/{pool.Id}/confirm", superAdmin.AccessToken));
        await (await TryConfirmAsync(superAdmin, confirm, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        (await VosAsync(superAdmin, project.Id)).Single(v => v.Id == terrace.Id).Status.ShouldBe(DesignVoStatus.Pending);

        var (again, _) = await ProposeAsync(superAdmin, "propose_design_vo", new { step = "confirm", vo_id = terrace.Id });
        await ConfirmAsync(superAdmin, again, project.Title);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(16500m);

        // one that is already part of the price is refused before any card
        var (_, applied) = await ProposeAsync(superAdmin, "propose_design_vo", new { step = "cancel", vo_id = terrace.Id });
        Message(applied).ShouldContain("already part of the price");

        var extra = await MakeAsync(superAdmin, project.Id, 700m);
        var (cancel, _) = await ProposeAsync(superAdmin, "propose_design_vo", new { step = "cancel", vo_id = extra.Id });
        cancel.PendingAction!.Preview.ShouldSatisfyAllConditions(p => p.Phrase.ShouldBe(project.Title), p => p.Irreversible.ShouldBeTrue());
        await ConfirmAsync(superAdmin, cancel, project.Title);
        (await VosAsync(superAdmin, project.Id)).Single(v => v.Id == extra.Id).Status.ShouldBe(DesignVoStatus.Cancelled);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(16500m);
    }

    [Fact]
    public async Task A_VO_the_profit_floor_refuses_is_kept_as_a_blocked_change_and_the_VO_stays_pending()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 1000m);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Tiles", amount = 900m, currency = "USD", spentOn = DateTime.UtcNow.ToString("yyyy-MM-dd") }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var (create, _) = await ProposeAsync(superAdmin, "propose_design_vo", new
        {
            step = "create", project_id = project.Id, title = "Smaller scope", apply = true, lines = new[] { new { description = "No interiors", amount = -500m } },
        }, "Apply a VO taking 500 off for no interiors");
        create.PendingAction!.Preview.Note!.ShouldContain("profit floor");

        var refused = await (await TryConfirmAsync(superAdmin, create, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        refused.Message.ShouldContain("propose_blocked_change");
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(1000m);
        (await VosAsync(superAdmin, project.Id)).ShouldHaveSingleItem().Status.ShouldBe(DesignVoStatus.Pending);

        var blockedId = await _steps.ScalarAsync<long>("SELECT id FROM blocked_changes WHERE project_id = @p", ("@p", project.Id));
        var blocked = await OkAsync<BlockedChangeResponse>(Client.GetAsync($"/api/blocked-changes/{blockedId}", superAdmin.AccessToken));
        (blocked.Kind, blocked.Status).ShouldBe((BlockedChangeKind.DesignVo, BlockedChangeStatus.Pending));

        // confirming the pending VO later is refused the same way, and kept
        var (confirm, _) = await ProposeAsync(superAdmin, "propose_design_vo", new { step = "confirm", vo_id = (await VosAsync(superAdmin, project.Id))[0].Id });
        await (await TryConfirmAsync(superAdmin, confirm, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM blocked_changes WHERE project_id = @p", ("@p", project.Id))).ShouldBe(2);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(1000m);
    }

    [Fact]
    public async Task A_VO_is_emailed_only_to_an_address_the_user_typed_with_the_address_typed_to_confirm()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 10000m);
        var vo = await MakeAsync(superAdmin, project.Id, 1500m);
        var to = AuthTestSteps.NewEmail("client-vo");

        var (_, guessed) = await ProposeAsync(superAdmin, "propose_send_design_vo", new { vo_id = vo.Id, to }, "Send the terrace VO to the client");
        Message(guessed).ShouldContain("only when the user typed it");

        var (send, _) = await ProposeAsync(superAdmin, "propose_send_design_vo", new { vo_id = vo.Id, to, note = "Sent after the call" }, $"Email the terrace VO to {to}");
        var card = send.PendingAction!;
        card.Preview.ShouldSatisfyAllConditions(
            p => p.Confirm.ShouldBe(ProtonConfirm.Typed),
            p => p.Phrase.ShouldBe(to),
            p => p.Irreversible.ShouldBeTrue(),
            p => p.Reach!.ShouldContain("outside the company"),
            p => p.Note!.ShouldContain("still pending"));
        card.Preview.Changes.Select(c => c.Label).ShouldBe(["To", "Sends", "Greeting", "Total the client reads", "Note kept with the delivery"]);
        card.Preview.Changes.Single(c => c.Label == "Total the client reads").After.ShouldContain("USD 1,665");
        api.Emails.CountTo(to).ShouldBe(0);

        await ConfirmAsync(superAdmin, send, to);
        api.Emails.CountTo(to).ShouldBe(1);
        // it only sends: the VO is as it was
        (await VosAsync(superAdmin, project.Id)).ShouldHaveSingleItem().Status.ShouldBe(DesignVoStatus.Pending);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(10000m);

        // changed after the card: the email would say something else than the card did
        var (stale, _) = await ProposeAsync(superAdmin, "propose_send_design_vo", new { vo_id = vo.Id, to }, $"Email the terrace VO to {to}");
        await OkAsync<DesignVoResponse>(Client.PostEmptyAsync($"/api/design-vos/{vo.Id}/confirm", superAdmin.AccessToken));
        await (await TryConfirmAsync(superAdmin, stale, to)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        api.Emails.CountTo(to).ShouldBe(1);

        // a cancelled one is not sent
        var cancelled = await MakeAsync(superAdmin, project.Id, 300m);
        await OkAsync<DesignVoResponse>(Client.PostEmptyAsync($"/api/design-vos/{cancelled.Id}/cancel", superAdmin.AccessToken));
        var (_, nothing) = await ProposeAsync(superAdmin, "propose_send_design_vo", new { vo_id = cancelled.Id, to }, $"Email the VO to {to}");
        Message(nothing).ShouldContain("cancelled");
    }

    [Fact]
    public async Task Whoever_may_only_see_the_money_is_offered_no_VO_card()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, 10000m);
        var vo = await MakeAsync(superAdmin, project.Id, 1500m);
        var viewer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView);
        var writer = await AdminWithAsync(superAdmin, PermissionCodes.FinancialView, PermissionCodes.FinancialWrite);
        var to = AuthTestSteps.NewEmail("client-vo");

        // financial:view reads them, but writes none of them
        (await ProposeAsync(viewer, "list_design_vos", new { project_id = project.Id }, "List the VOs")).Result["data"].ShouldNotBeNull();
        foreach (var (tool, input) in new (string, object)[]
        {
            ("propose_design_vo", new { step = "confirm", vo_id = vo.Id }),
            ("propose_design_vo", new { step = "cancel", vo_id = vo.Id }),
            ("propose_send_design_vo", new { vo_id = vo.Id, to }),
        })
        {
            var (_, unavailable) = await ProposeAsync(viewer, tool, input, $"Do it for {to}");
            Message(unavailable).ShouldContain("isn't available to this user");
        }

        // financial:write on top does
        var (card, _) = await ProposeAsync(writer, "propose_design_vo", new { step = "confirm", vo_id = vo.Id });
        card.PendingAction.ShouldNotBeNull();
        await ConfirmAsync(writer, card, project.Title);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(11500m);

        // taken away after the card, it is checked again at Confirm
        var other = await MakeAsync(superAdmin, project.Id, 400m);
        var (cancel, _) = await ProposeAsync(writer, "propose_design_vo", new { step = "cancel", vo_id = other.Id });
        var revoked = await Client.PutJsonAsync($"/api/users/{writer.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialWrite, action = "revoke", reason = "No longer" } } }, superAdmin.AccessToken);
        revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync());
        (await TryConfirmAsync(writer, cancel, project.Title)).IsSuccessStatusCode.ShouldBeFalse();
        (await VosAsync(superAdmin, project.Id)).Single(v => v.Id == other.Id).Status.ShouldBe(DesignVoStatus.Pending);
    }
}
