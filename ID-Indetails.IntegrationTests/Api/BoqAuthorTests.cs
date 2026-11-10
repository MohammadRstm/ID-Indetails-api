using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;
using static ID_Indetails.IntegrationTests.Api.BoqTestSteps;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// BOQ authors in either business: a Design employee granted boq:create reaches Execution's active client sites only for
/// their own drafts, submits them to the site's pricing managers and owner, and keeps or withdraws what they change.
/// </summary>
public class BoqAuthorTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    private async Task GrantAsync(AuthResponse superAdmin, long userId, string code)
    {
        var granted = await Client.PutJsonAsync($"/api/users/{userId}/permissions",
            new { changes = new[] { new { code, action = "grant", reason = "Writes BOQs" } } }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
    }

    /// <summary>A Design employee who writes BOQs, signed in again so their token holds the grant.</summary>
    private async Task<AuthResponse> DesignAuthorAsync(AuthResponse superAdmin)
    {
        var (email, password, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await GrantAsync(superAdmin, employee.User.Id, PermissionCodes.BoqCreate);
        return await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldSignInAsync();
    }

    private Task<ProjectResponse> NewSiteAsync(AuthResponse by) => Projects.CreateProjectAsync(by.AccessToken, DomainCodes.Execution, projectFee: 0m);

    private Task<BoqListResponse> ListAsync(AuthResponse by, long siteId) => OkAsync<BoqListResponse>(Client.GetAsync($"/api/projects/{siteId}/boqs", by.AccessToken));

    private Task<BoqDetailResponse> PostAsync(AuthResponse by, long boqId, string step) => OkAsync<BoqDetailResponse>(Client.PostEmptyAsync($"/api/boqs/{boqId}/{step}", by.AccessToken));

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind).ToList();

    [Fact]
    public async Task A_Design_employee_writes_drafts_for_Executions_active_sites_and_sees_nothing_else_of_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var author = await DesignAuthorAsync(superAdmin);
        var site = await NewSiteAsync(superAdmin);

        // holding boq:create doesn't open Execution
        (await OkAsync<CurrentUserResponse>(Client.GetAsync("/api/auth/me", author.AccessToken))).VisibleDomains.ShouldBe([DomainCodes.Design]);
        await (await Client.GetAsync($"/api/projects/{site.Id}", author.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // the BOQs page lists the active client sites by name and location
        var sites = await OkAsync<List<BoqSiteResponse>>(Client.GetAsync("/api/boq-sites", author.AccessToken));
        sites.ShouldContain(s => s.Id == site.Id && s.Title == site.Title);
        (await OkAsync<BoqSiteResponse>(Client.GetAsync($"/api/boq-sites/{site.Id}", author.AccessToken))).Id.ShouldBe(site.Id);

        // there they write their own drafts, and see none of the money or anyone else's work
        var draft = await Boqs.CreateAsync(author.AccessToken, site.Id, "internal");
        await Boqs.SaveAsync(author.AccessToken, draft.Id, [Line("Concrete", 100, 76m, 0.1m, "A 2.1.1", "m3")], "Engineer estimate");
        var list = await ListAsync(author, site.Id);
        list.Internal.ShouldHaveSingleItem().Id.ShouldBe(draft.Id);
        list.Client.ShouldBeEmpty();
        list.Price.ShouldBeNull();
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/boqs", new { kind = "client" }, author.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // a site on hold, an internal site or a Design project isn't theirs to reach
        await _steps.ExecuteAsync("UPDATE projects SET status = 'hold' WHERE id = @id", ("@id", site.Id));
        (await OkAsync<List<BoqSiteResponse>>(Client.GetAsync("/api/boq-sites", author.AccessToken))).ShouldNotContain(s => s.Id == site.Id);
        await (await Client.GetAsync($"/api/projects/{site.Id}/boqs", author.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/boq-sites/{site.Id}", author.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // without boq:create there is no BOQs page
        var (_, _, plain) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        ((int)(await Client.GetAsync("/api/boq-sites", plain.AccessToken)).StatusCode).ShouldBe(403);
    }

    [Fact]
    public async Task A_submitted_draft_is_edited_by_pricing_and_the_owner_and_its_author_keeps_or_withdraws_the_changes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var author = await DesignAuthorAsync(superAdmin);
        var other = await DesignAuthorAsync(superAdmin);
        var site = await NewSiteAsync(superAdmin);
        var draft = await Boqs.CreateAsync(author.AccessToken, site.Id, "internal");
        var saved = await Boqs.SaveAsync(author.AccessToken, draft.Id,
            [Line("Concrete", 100, 76m, 0.1m, "A 2.1.1", "m3"), Line("Steel", 9000, 0.69m, 0.1m, "A 2.1.2", "kg")], "Engineer estimate");
        var privateOne = await Boqs.CreateAsync(author.AccessToken, site.Id, "internal", "Still thinking");

        // submitted: the pricing managers (the super admin) see it; the private one and other authors' views stay as they were
        var submitted = await PostAsync(author, draft.Id, "submit");
        submitted.SubmittedAt.ShouldNotBeNull();
        (await ListAsync(superAdmin, site.Id)).Internal.Select(b => b.Id).ShouldBe([draft.Id]);
        (await ListAsync(other, site.Id)).Internal.ShouldBeEmpty();
        (await OfKindAsync(superAdmin, NotificationKinds.BoqSubmitted)).ShouldContain(n => n.SubjectId == draft.Id);
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/submit", author.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqAlreadySubmitted);

        // its author only reads it now, and can't delete it
        await (await Client.PutJsonAsync($"/api/boqs/{draft.Id}", new { title = "Mine", adjustmentType = "none", lines = Array.Empty<object>() }, author.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/boqs/{draft.Id}", author.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        // nor does anyone else delete it
        await (await Client.DeleteAsync($"/api/boqs/{draft.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // a pricing manager reprices it: the author is told, and sees what changed since they submitted it
        var concrete = saved.Lines.Single(l => l.Description == "Concrete");
        var steel = saved.Lines.Single(l => l.Description == "Steel");
        await Boqs.SaveAsync(superAdmin.AccessToken, draft.Id,
            [Line("Concrete", 100, 80m, 0.1m, "A 2.1.1", "m3", concrete.Id), Line("Formwork", 300, 18m, 0.1m, "A 2.1.4", "m2")], "Engineer estimate");
        (await OfKindAsync(author, NotificationKinds.BoqDraftEdited)).ShouldHaveSingleItem().SubjectId.ShouldBe(draft.Id);
        var seen = await Boqs.GetAsync(author.AccessToken, draft.Id);
        var changes = seen.Changes.ShouldNotBeNull();
        changes.Single(c => c.Kind == "changed").ShouldSatisfyAllConditions(
            c => c.LineId.ShouldBe(concrete.Id),
            c => c.Fields.ShouldHaveSingleItem().ShouldBe(new BoqFieldChangeResponse("rate", "76", "80")));
        changes.Single(c => c.Kind == "added").Description.ShouldBe("Formwork");
        changes.Single(c => c.Kind == "removed").Description.ShouldBe(steel.Description);
        (await ListAsync(author, site.Id)).Internal.Single(b => b.Id == draft.Id).Changed.ShouldBeTrue();

        // kept: the edited lines are the new starting point
        (await PostAsync(author, draft.Id, "keep")).Changes.ShouldNotBeNull().ShouldBeEmpty();

        // withdrawn: private again, out of the pricing managers' sight, and the author's to change
        var withdrawn = await PostAsync(author, draft.Id, "withdraw-submission");
        withdrawn.SubmittedAt.ShouldBeNull();
        withdrawn.Changes.ShouldBeNull();
        (await ListAsync(superAdmin, site.Id)).Internal.ShouldBeEmpty();
        await Boqs.SaveAsync(author.AccessToken, draft.Id, [Line("Concrete", 110, 80m, 0.1m, "A 2.1.1", "m3")], "Engineer estimate");
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/keep", author.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqNotSubmitted);
        await (await Client.PostEmptyAsync($"/api/boqs/{privateOne.Id}/submit", author.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.BoqEmpty);
        // only its author submits it
        await (await Client.PostEmptyAsync($"/api/boqs/{draft.Id}/submit", other.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task The_sites_owner_sees_and_edits_only_its_submitted_drafts()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var author = await DesignAuthorAsync(superAdmin);
        var (_, _, owner) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await NewSiteAsync(superAdmin);
        await _steps.ExecuteAsync("UPDATE projects SET owner_id = @o WHERE id = @p", ("@o", owner.User.Id), ("@p", site.Id));
        var draft = await Boqs.CreateAsync(author.AccessToken, site.Id, "internal");
        await Boqs.SaveAsync(author.AccessToken, draft.Id, [Line("Excavation", 100, 8m)], "Estimate");

        (await ListAsync(owner, site.Id)).Internal.ShouldBeEmpty();
        await PostAsync(author, draft.Id, "submit");
        (await ListAsync(owner, site.Id)).Internal.ShouldHaveSingleItem().Id.ShouldBe(draft.Id);
        (await OfKindAsync(owner, NotificationKinds.BoqSubmitted)).ShouldContain(n => n.SubjectId == draft.Id);
        var lineId = (await Boqs.GetAsync(owner.AccessToken, draft.Id)).Lines.ShouldHaveSingleItem().Id;
        var edited = await Boqs.SaveAsync(owner.AccessToken, draft.Id, [Line("Excavation", 120, 8m, id: lineId)], "Estimate");
        edited.TotalUsd.ShouldBe(960m);
        (await OfKindAsync(author, NotificationKinds.BoqDraftEdited)).ShouldContain(n => n.SubjectId == draft.Id);

        // another admin of Execution, not its owner, sees none of it
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        await (await Client.GetAsync($"/api/projects/{site.Id}/boqs", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
