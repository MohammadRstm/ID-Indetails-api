using System.Net;
using System.Net.Http.Headers;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Publishing a client portal: staff work on a draft and preview it; clients see only what was published, all of it
/// at once, on projects they may open; the hero, the order and the introduction follow the same rule.
/// </summary>
public class PortalPublishingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE1, .. new byte[300]];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[100]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    private sealed record Setup(AuthResponse Staff, ProjectResponse Project, long ClientId, string ContactEmail, string ClientToken);

    // ---- set-up ----

    /// <summary>A super admin, a Design client project, and the client signed in to the portal with access to it.</summary>
    private async Task<Setup> ProjectWithClientAsync()
    {
        var (_, _, staff) = await _steps.ActiveSuperAdminAsync();
        var contact = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = contact, label = "Office" } },
        }, staff.AccessToken)).ReadAsync<IdOnly>();
        var project = await ProjectAsync(staff, DomainCodes.Design, client.Id);
        var token = await GrantAsync(staff, project.Id, contact, firstTime: true);
        return new Setup(staff, project, client.Id, contact, token);
    }

    private async Task<ProjectResponse> ProjectAsync(AuthResponse staff, string domain, long? clientId)
    {
        var title = "Project " + Guid.NewGuid().ToString("N")[..8];
        object body = clientId is null ? new { title, domainCode = domain, billingType = "internal" } : new { title, domainCode = domain, clientId };
        var response = await Client.PostJsonAsync("/api/projects", body, staff.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    /// <summary>Gives the contact access to the project; the first time through an invitation they accept.</summary>
    private async Task<string> GrantAsync(AuthResponse staff, long projectId, string contact, bool firstTime)
    {
        var invited = await Client.PostJsonAsync($"/api/projects/{projectId}/portal/invitations", new { email = contact }, staff.AccessToken);
        invited.IsSuccessStatusCode.ShouldBeTrue(await invited.Content.ReadAsStringAsync());
        if (!firstTime)
        {
            return string.Empty;
        }

        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(contact), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase",
        });
        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());
        return (await accepted.ReadAsync<PortalAuthResponse>()).AccessToken;
    }

    private static string Base(long projectId) => $"/api/projects/{projectId}/portal/content";

    private async Task<PortalUploadCompletedResponse> UploadAsync(
        AuthResponse staff, long projectId, string fileName, byte[] bytes, string? section = "presentation", long? replaceItemId = null, bool hero = false)
    {
        var start = await Client.PostJsonAsync($"{Base(projectId)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), section = hero ? null : section, hero, fileName, size = bytes.Length, replaceItemId },
            staff.AccessToken);
        ((int)start.StatusCode).ShouldBe(201, await start.Content.ReadAsStringAsync());
        var started = await start.ReadAsync<PortalUploadStartedResponse>();
        var links = await (await Client.PostJsonAsync($"{Base(projectId)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, staff.AccessToken))
            .ReadAsync<PortalPartLinksResponse>();
        (await Client.PutAsync(links.Parts[0].Url, new ByteArrayContent(bytes))).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await Client.PostEmptyAsync($"{Base(projectId)}/uploads/{started.UploadId}/complete", staff.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        return await completed.ReadAsync<PortalUploadCompletedResponse>();
    }

    private async Task<PortalContentResponse> ContentAsync(AuthResponse staff, long projectId)
    {
        var response = await Client.GetAsync(Base(projectId), staff.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalContentResponse>();
    }

    private async Task<PortalContentResponse> ChangeAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalContentResponse>();
    }

    private async Task<PortalContentResponse> PublishAsync(AuthResponse staff, long projectId)
    {
        var revision = (await ContentAsync(staff, projectId)).Revision;
        return await ChangeAsync(await Client.PostJsonAsync($"{Base(projectId)}/publish", new { expectedRevision = revision }, staff.AccessToken));
    }

    private async Task<PortalPresentationResponse> PreviewAsync(AuthResponse staff, long projectId)
    {
        var response = await Client.GetAsync($"{Base(projectId)}/preview", staff.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalPresentationResponse>();
    }

    private Task<HttpResponseMessage> AsClientAsync(string clientToken, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
        return Client.SendAsync(request);
    }

    private async Task<PortalPresentationResponse> ClientViewAsync(string clientToken, long projectId)
    {
        var response = await AsClientAsync(clientToken, $"/api/portal/projects/{projectId}");
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalPresentationResponse>();
    }

    private static IReadOnlyList<string> Titles(PortalPresentationResponse presentation, PortalSection section)
        => presentation.Sections.Single(s => s.Section == section).Items.Select(i => i.Title).ToList();

    // ---- tests ----

    [Fact]
    public async Task Clients_see_nothing_before_the_first_publish_and_then_only_what_was_published()
    {
        var s = await ProjectWithClientAsync();
        var living = await UploadAsync(s.Staff, s.Project.Id, "Living room.jpg", Jpeg);

        var before = await ClientViewAsync(s.ClientToken, s.Project.Id);
        before.IsPublished.ShouldBeFalse();
        before.Sections.Select(x => x.Section).ShouldBe([PortalSection.Presentation, PortalSection.Model, PortalSection.Drawing]);
        before.Sections.ShouldAllBe(x => x.Items.Count == 0);
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}/items/{living.Item!.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // the staff preview shows the draft, in the client's shape
        var preview = await PreviewAsync(s.Staff, s.Project.Id);
        preview.IsPublished.ShouldBeTrue();
        Titles(preview, PortalSection.Presentation).ShouldBe(["Living room"]);

        var published = await PublishAsync(s.Staff, s.Project.Id);
        published.HasUnpublishedChanges.ShouldBeFalse();
        published.Publication.PublishedAt.ShouldNotBeNull();
        published.Publication.PublishedBy!.Id.ShouldBe(s.Staff.User.Id);
        published.Items.Single().State.ShouldBe(PortalItemState.Unchanged);

        var after = await ClientViewAsync(s.ClientToken, s.Project.Id);
        after.IsPublished.ShouldBeTrue();
        after.Title.ShouldBe(s.Project.Title);
        Titles(after, PortalSection.Presentation).ShouldBe(["Living room"]);
        var link = await (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}/items/{living.Item.Id}/link?inline=false"))
            .ReadAsync<PortalContentLinkResponse>();
        (await (await Client.GetAsync(link.Url)).Content.ReadAsByteArrayAsync()).ShouldBe(Jpeg);

        // a new draft stays the staff's until the next publish
        var plan = await UploadAsync(s.Staff, s.Project.Id, "Plan.pdf", Pdf, "drawing");
        Titles(await ClientViewAsync(s.ClientToken, s.Project.Id), PortalSection.Drawing).ShouldBeEmpty();
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Drawing).ShouldBe(["Plan"]);
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}/items/{plan.Item!.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var content = await ContentAsync(s.Staff, s.Project.Id);
        content.Changes.Added.ShouldBe(1);
        content.CanPublish.ShouldBeTrue();
    }

    [Fact]
    public async Task A_later_publish_changes_everything_at_once_and_lets_old_files_go()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        var bedroom = await UploadAsync(s.Staff, s.Project.Id, "Bedroom.jpg", Jpeg);
        var old = await UploadAsync(s.Staff, s.Project.Id, "Old plan.pdf", Pdf, "drawing");
        await PublishAsync(s.Staff, s.Project.Id);
        var oldKitchenUrl = (await ClientViewAsync(s.ClientToken, s.Project.Id)).Sections[0].Items.Single(i => i.Id == kitchen.Item!.Id).PreviewUrl!;

        // a new file for the kitchen, a new title, a new order, a removal and an addition
        await UploadAsync(s.Staff, s.Project.Id, "Kitchen v2.jpg", OtherJpeg, replaceItemId: kitchen.Item!.Id);
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{bedroom.Item!.Id}", new { title = "Main bedroom" }, s.Staff.AccessToken));
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { bedroom.Item.Id, kitchen.Item.Id } }, s.Staff.AccessToken));
        (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{old.Item!.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await UploadAsync(s.Staff, s.Project.Id, "New plan.pdf", Pdf, "drawing");

        var draft = await ContentAsync(s.Staff, s.Project.Id);
        draft.Changes.ShouldBe(new PortalChangesResponse(1, 2, 1, draft.Changes.Reordered, false, false));
        draft.Changes.Reordered.ShouldBe([PortalSection.Presentation]);
        draft.Items.Single(i => i.Id == old.Item.Id).State.ShouldBe(PortalItemState.Removed);

        // clients still see the old version, old kitchen file included
        var before = await ClientViewAsync(s.ClientToken, s.Project.Id);
        Titles(before, PortalSection.Presentation).ShouldBe(["Kitchen", "Bedroom"]);
        Titles(before, PortalSection.Drawing).ShouldBe(["Old plan"]);
        (await Client.GetAsync(oldKitchenUrl)).IsSuccessStatusCode.ShouldBeTrue();

        await PublishAsync(s.Staff, s.Project.Id);

        var after = await ClientViewAsync(s.ClientToken, s.Project.Id);
        Titles(after, PortalSection.Presentation).ShouldBe(["Main bedroom", "Kitchen"]);
        Titles(after, PortalSection.Drawing).ShouldBe(["New plan"]);
        // the kitchen's old file and the removed plan are gone from the store
        (await Client.GetAsync(oldKitchenUrl)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_items WHERE id = @id", ("@id", old.Item.Id))).ShouldBe(0);
        (await ContentAsync(s.Staff, s.Project.Id)).HasUnpublishedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task A_publish_of_an_older_revision_is_refused_and_clients_keep_the_previous_version()
    {
        var s = await ProjectWithClientAsync();
        await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        await PublishAsync(s.Staff, s.Project.Id);

        var seen = (await ContentAsync(s.Staff, s.Project.Id)).Revision;
        // someone else adds a file after the page was loaded
        await UploadAsync(s.Staff, s.Project.Id, "Bedroom.jpg", Jpeg);

        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/publish", new { expectedRevision = seen }, s.Staff.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.PortalRevisionStale);
        Titles(await ClientViewAsync(s.ClientToken, s.Project.Id), PortalSection.Presentation).ShouldBe(["Kitchen"]);
    }

    [Fact]
    public async Task The_order_is_kept_and_an_order_from_an_older_page_changes_nothing()
    {
        var s = await ProjectWithClientAsync();
        var a = await UploadAsync(s.Staff, s.Project.Id, "A.jpg", Jpeg);
        var b = await UploadAsync(s.Staff, s.Project.Id, "B.jpg", Jpeg);
        var c = await UploadAsync(s.Staff, s.Project.Id, "C.jpg", Jpeg);

        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { c.Item!.Id, a.Item!.Id, b.Item!.Id } }, s.Staff.AccessToken));
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBe(["C", "A", "B"]);

        // a page that didn't know about D sends an order without it
        await UploadAsync(s.Staff, s.Project.Id, "D.jpg", Jpeg);
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { a.Item.Id, b.Item.Id, c.Item.Id } }, s.Staff.AccessToken)).ShouldFailWithAsync(409, ErrorCodes.PortalOrderStale);
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBe(["C", "A", "B", "D"]);

        // another section's files can't be ordered here
        var plan = await UploadAsync(s.Staff, s.Project.Id, "Plan.pdf", Pdf, "drawing");
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { plan.Item!.Id } }, s.Staff.AccessToken)).ShouldFailWithAsync(409, ErrorCodes.PortalOrderStale);
    }

    [Fact]
    public async Task The_hero_is_an_image_kept_by_its_file_and_reaches_clients_when_published()
    {
        var s = await ProjectWithClientAsync();
        var living = await UploadAsync(s.Staff, s.Project.Id, "Living room.jpg", Jpeg);
        var plan = await UploadAsync(s.Staff, s.Project.Id, "Plan.pdf", Pdf, "drawing");

        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/hero", new { itemId = plan.Item!.Id }, s.Staff.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PortalHeroNotImage);
        var chosen = await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/hero", new { itemId = living.Item!.Id }, s.Staff.AccessToken));
        chosen.Hero!.FileId.ShouldBe(living.Item.File.Id);
        chosen.Items.Single(i => i.Id == living.Item.Id).IsHero.ShouldBeTrue();
        chosen.Changes.HeroChanged.ShouldBeTrue();

        // deleting the item doesn't take the hero with it: the hero keeps the file
        (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{living.Item.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await PreviewAsync(s.Staff, s.Project.Id)).Hero.ShouldNotBeNull();
        (await ContentAsync(s.Staff, s.Project.Id)).Hero!.FileId.ShouldBe(living.Item.File.Id);

        // an uploaded hero takes its place, and the old one, used by nothing now, goes
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), hero = true, fileName = "cover.pdf", size = Pdf.Length }, s.Staff.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PortalHeroNotImage);
        var uploaded = await UploadAsync(s.Staff, s.Project.Id, "Cover.jpg", OtherJpeg, hero: true);
        uploaded.Item.ShouldBeNull();
        var withCover = await ContentAsync(s.Staff, s.Project.Id);
        withCover.Hero!.Name.ShouldBe("Cover.jpg");
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_files WHERE id = @id", ("@id", living.Item.File.Id))).ShouldBe(0);

        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Hero.ShouldBeNull();
        await PublishAsync(s.Staff, s.Project.Id);
        var seen = await ClientViewAsync(s.ClientToken, s.Project.Id);
        seen.Hero!.Name.ShouldBe("Cover.jpg");
        (await Client.GetAsync(seen.Hero.Url)).IsSuccessStatusCode.ShouldBeTrue();

        // clearing it is a draft change too
        var cleared = await ChangeAsync(await Client.DeleteAsync($"{Base(s.Project.Id)}/hero", s.Staff.AccessToken));
        cleared.Hero.ShouldBeNull();
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Hero.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_removed_file_stays_for_clients_until_the_next_publish_and_can_be_undone()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        await PublishAsync(s.Staff, s.Project.Id);

        (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{kitchen.Item!.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        Titles(await ClientViewAsync(s.ClientToken, s.Project.Id), PortalSection.Presentation).ShouldBe(["Kitchen"]);
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBeEmpty();

        var restored = await ChangeAsync(await Client.PostEmptyAsync($"{Base(s.Project.Id)}/items/{kitchen.Item.Id}/restore", s.Staff.AccessToken));
        restored.Items.Single().State.ShouldBe(PortalItemState.Unchanged);
        restored.HasUnpublishedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Discarding_puts_the_draft_back_to_what_clients_see()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        var revision = (await ContentAsync(s.Staff, s.Project.Id)).Revision;
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/discard", new { expectedRevision = revision }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalNeverPublished);

        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Welcome to your villa." }, s.Staff.AccessToken));
        await PublishAsync(s.Staff, s.Project.Id);

        await UploadAsync(s.Staff, s.Project.Id, "Kitchen v2.jpg", OtherJpeg, replaceItemId: kitchen.Item!.Id);
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{kitchen.Item.Id}", new { title = "Renamed", section = "model" }, s.Staff.AccessToken));
        var extra = await UploadAsync(s.Staff, s.Project.Id, "Extra.jpg", Jpeg);
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Something else" }, s.Staff.AccessToken));

        var draft = await ContentAsync(s.Staff, s.Project.Id);
        draft.CanDiscard.ShouldBeTrue();
        var discarded = await ChangeAsync(await Client.PostJsonAsync($"{Base(s.Project.Id)}/discard", new { expectedRevision = draft.Revision }, s.Staff.AccessToken));

        discarded.HasUnpublishedChanges.ShouldBeFalse();
        discarded.Summary.ShouldBe("Welcome to your villa.");
        var item = discarded.Items.Single();
        item.Id.ShouldBe(kitchen.Item.Id);
        item.Title.ShouldBe("Kitchen");
        item.Section.ShouldBe(PortalSection.Presentation);
        item.File.Id.ShouldBe(kitchen.Item.File.Id);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_items WHERE id = @id", ("@id", extra.Item!.Id))).ShouldBe(0);
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Summary.ShouldBe("Welcome to your villa.");
    }

    [Fact]
    public async Task A_completed_project_stays_open_and_an_archived_one_cant_change_or_publish_but_its_clients_still_see_it()
    {
        var s = await ProjectWithClientAsync();
        await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        await PublishAsync(s.Staff, s.Project.Id);

        // completed: the client keeps the presentation, and the team can still hand over more
        var completed = await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/complete", s.Staff.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Status.ShouldBe(ProjectStatus.Completed);
        await UploadAsync(s.Staff, s.Project.Id, "Bedroom.jpg", Jpeg);
        (await ContentAsync(s.Staff, s.Project.Id)).CanPublish.ShouldBeTrue();

        var archived = await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/archive", s.Staff.AccessToken);
        archived.IsSuccessStatusCode.ShouldBeTrue(await archived.Content.ReadAsStringAsync());

        var content = await ContentAsync(s.Staff, s.Project.Id);
        content.CanEdit.ShouldBeFalse();
        content.CanPublish.ShouldBeFalse();
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/publish", new { expectedRevision = content.Revision }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalContentReadOnly);
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "x" }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalContentReadOnly);
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/sections/presentation/order", new { itemIds = content.Items.Select(i => i.Id) }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalContentReadOnly);

        var seen = await ClientViewAsync(s.ClientToken, s.Project.Id);
        seen.Status.ShouldBe(ProjectStatus.Archived);
        Titles(seen, PortalSection.Presentation).ShouldBe(["Kitchen"]);
    }

    [Fact]
    public async Task Clients_reach_only_published_files_of_projects_they_can_open()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        await PublishAsync(s.Staff, s.Project.Id);

        // another project of the same client, without access, and its published file
        var other = await ProjectAsync(s.Staff, DomainCodes.Design, s.ClientId);
        var otherFile = await UploadAsync(s.Staff, other.Id, "Other.jpg", Jpeg);
        await PublishAsync(s.Staff, other.Id);
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{other.Id}/items/{otherFile.Item!.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // another project's item through this project
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}/items/{otherFile.Item.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // the client's token opens no staff endpoint, and a staff token no client one
        (await AsClientAsync(s.ClientToken, $"{Base(s.Project.Id)}/preview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync($"/api/portal/projects/{s.Project.Id}", s.Staff.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // access ended: nothing, even what they saw before
        var clients = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.Staff.AccessToken)).ReadAsync<PortalClientsResponse>();
        var accessId = clients.Clients.Single(c => c.Email == s.ContactEmail).AccessId!.Value;
        // a second project keeps the account signed in, so the refusal below is about this project
        await GrantAsync(s.Staff, other.Id, s.ContactEmail, firstTime: false);
        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/access/{accessId}/revoke", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AsClientAsync(s.ClientToken, $"/api/portal/projects/{s.Project.Id}/items/{kitchen.Item!.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ClientViewAsync(s.ClientToken, other.Id)).IsPublished.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_people_who_manage_Design_client_portal_files_can_arrange_preview_or_publish()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Design);

        foreach (var outsider in new[] { employee, executionAdmin })
        {
            await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/publish", new { expectedRevision = 0 }, outsider.AccessToken))
                .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.GetAsync($"{Base(s.Project.Id)}/preview", outsider.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/hero", new { itemId = kitchen.Item!.Id }, outsider.AccessToken))
                .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }

        // a Design admin may: the same rule as every other project change
        (await PublishAsync(designAdmin, s.Project.Id)).HasUnpublishedChanges.ShouldBeFalse();

        // an Execution site has no client portal at all
        var siteClient = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Site client",
            label = "Client",
            domainCode = DomainCodes.Execution,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("site") } },
        }, s.Staff.AccessToken)).ReadAsync<IdOnly>();
        var site = await ProjectAsync(s.Staff, DomainCodes.Execution, siteClient.Id);
        await (await Client.GetAsync($"{Base(site.Id)}/preview", s.Staff.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"{Base(site.Id)}/publish", new { expectedRevision = 0 }, s.Staff.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
