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
/// Publishing a client portal: staff choose project files for the client, arrange the draft and preview it; clients
/// see only what was published, all of it at once, on projects they may open; the hero, the order and the introduction
/// follow the same rule.
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

    private static string Files(long projectId) => $"/api/projects/{projectId}/files";

    /// <summary>Uploads a file to the project's files and, unless it is a hero, chooses it for the client.</summary>
    private async Task<ProjectFileUploadCompletedResponse> UploadAsync(
        AuthResponse staff, long projectId, string fileName, byte[] bytes, string? section = "presentation", long? replaceItemId = null, bool hero = false)
    {
        var start = await Client.PostJsonAsync($"{Files(projectId)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), section = hero ? null : section, hero, fileName, size = bytes.Length, replaceItemId },
            staff.AccessToken);
        ((int)start.StatusCode).ShouldBe(201, await start.Content.ReadAsStringAsync());
        var started = await start.ReadAsync<PortalUploadStartedResponse>();
        var links = await (await Client.PostJsonAsync($"{Files(projectId)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, staff.AccessToken))
            .ReadAsync<PortalPartLinksResponse>();
        (await Client.PutAsync(links.Parts[0].Url, new ByteArrayContent(bytes))).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await Client.PostEmptyAsync($"{Files(projectId)}/uploads/{started.UploadId}/complete", staff.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        var result = await completed.ReadAsync<ProjectFileUploadCompletedResponse>();
        if (result.Item is { } item)
        {
            await ChooseAsync(staff, projectId, item.Id, forClient: true);
        }

        return result;
    }

    private async Task ChooseAsync(AuthResponse staff, long projectId, long itemId, bool forClient)
        => await OkAsync(await Client.PutJsonAsync($"{Files(projectId)}/items/{itemId}/for-client", new { forClient }, staff.AccessToken));

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

    private static async Task OkAsync(HttpResponseMessage response)
        => response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());

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

    private async Task<PortalContentResponse> AddHeroAsync(AuthResponse staff, long projectId, long itemId)
        => await ChangeAsync(await Client.PostJsonAsync($"{Base(projectId)}/heroes", new { itemId }, staff.AccessToken));

    private async Task<PortalContentResponse> OrderHeroesAsync(AuthResponse staff, long projectId, params long[] fileIds)
        => await ChangeAsync(await Client.PutJsonAsync($"{Base(projectId)}/heroes/order", new { fileIds }, staff.AccessToken));

    private Task<long> LiveFilesAsync(long fileId)
        => _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_files WHERE id = @id AND deleted_at IS NULL", ("@id", fileId));

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
        await OkAsync(await Client.PutJsonAsync($"{Files(s.Project.Id)}/items/{bedroom.Item!.Id}", new { title = "Main bedroom" }, s.Staff.AccessToken));
        await OkAsync(await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { bedroom.Item.Id, kitchen.Item.Id } }, s.Staff.AccessToken));
        (await Client.DeleteAsync($"{Files(s.Project.Id)}/items/{old.Item!.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
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
        // someone else chooses another file for the client after the page was loaded
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

        await OkAsync(await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { c.Item!.Id, a.Item!.Id, b.Item!.Id } }, s.Staff.AccessToken));
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBe(["C", "A", "B"]);

        // a page that didn't know about D sends an order without it
        await UploadAsync(s.Staff, s.Project.Id, "D.jpg", Jpeg);
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { a.Item.Id, b.Item.Id, c.Item.Id } }, s.Staff.AccessToken)).ShouldFailWithAsync(409, ErrorCodes.PortalOrderStale);
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBe(["C", "A", "B", "D"]);

        // another section's files can't be ordered here
        var plan = await UploadAsync(s.Staff, s.Project.Id, "Plan.pdf", Pdf, "drawing");
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order",
            new { itemIds = new[] { plan.Item!.Id } }, s.Staff.AccessToken)).ShouldFailWithAsync(409, ErrorCodes.PortalOrderStale);
    }

    [Fact]
    public async Task Hero_images_are_added_ordered_and_removed_in_the_draft_and_reach_clients_when_published()
    {
        var s = await ProjectWithClientAsync();
        var living = await UploadAsync(s.Staff, s.Project.Id, "Living room.jpg", Jpeg);
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", OtherJpeg);
        var plan = await UploadAsync(s.Staff, s.Project.Id, "Plan.pdf", Pdf, "drawing");
        var livingFile = living.Item!.File.Id;
        var kitchenFile = kitchen.Item!.File.Id;

        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = plan.Item!.Id }, s.Staff.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PortalHeroNotImage);
        var first = await AddHeroAsync(s.Staff, s.Project.Id, living.Item.Id);
        first.Heroes.Select(h => h.FileId).ShouldBe([livingFile]);
        first.Hero!.FileId.ShouldBe(livingFile);
        first.Items.Single(i => i.Id == living.Item.Id).IsHero.ShouldBeTrue();
        first.Changes.HeroChanged.ShouldBeTrue();

        // an image is a hero once
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = living.Item.Id }, s.Staff.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.PortalHeroAlreadyAdded);

        // added images and uploaded ones go at the end
        await AddHeroAsync(s.Staff, s.Project.Id, kitchen.Item.Id);
        await (await Client.PostJsonAsync($"{Files(s.Project.Id)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), hero = true, fileName = "cover.pdf", size = Pdf.Length }, s.Staff.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PortalHeroNotImage);
        var uploaded = await UploadAsync(s.Staff, s.Project.Id, "Cover.jpg", OtherJpeg, hero: true);
        uploaded.Item.ShouldBeNull();
        var withCover = await ContentAsync(s.Staff, s.Project.Id);
        withCover.Heroes.Select(h => h.Name).ShouldBe(["Living room.jpg", "Kitchen.jpg", "Cover.jpg"]);
        var coverFile = withCover.Heroes[2].FileId;

        // a new order lists exactly the draft's images
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/heroes/order", new { fileIds = new[] { coverFile, livingFile } }, s.Staff.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.PortalOrderStale);
        var ordered = await OrderHeroesAsync(s.Staff, s.Project.Id, coverFile, livingFile, kitchenFile);
        ordered.Heroes.Select(h => h.FileId).ShouldBe([coverFile, livingFile, kitchenFile]);
        ordered.Hero!.Name.ShouldBe("Cover.jpg");
        var preview = await PreviewAsync(s.Staff, s.Project.Id);
        preview.Heroes.Select(h => h.Name).ShouldBe(["Cover.jpg", "Living room.jpg", "Kitchen.jpg"]);
        preview.Hero!.Name.ShouldBe("Cover.jpg");

        // clients see them only once published, in the same order
        var before = await ClientViewAsync(s.ClientToken, s.Project.Id);
        before.Heroes.ShouldBeEmpty();
        before.Hero.ShouldBeNull();
        await PublishAsync(s.Staff, s.Project.Id);
        var seen = await ClientViewAsync(s.ClientToken, s.Project.Id);
        seen.Heroes.Select(h => h.Name).ShouldBe(["Cover.jpg", "Living room.jpg", "Kitchen.jpg"]);
        seen.Hero!.Name.ShouldBe("Cover.jpg");
        (await Client.GetAsync(seen.Heroes[1].Url)).IsSuccessStatusCode.ShouldBeTrue();
        (await ContentAsync(s.Staff, s.Project.Id)).Changes.HeroChanged.ShouldBeFalse();

        // a new order undone is nothing to publish
        (await OrderHeroesAsync(s.Staff, s.Project.Id, livingFile, coverFile, kitchenFile)).Changes.HeroChanged.ShouldBeTrue();
        var undone = await OrderHeroesAsync(s.Staff, s.Project.Id, coverFile, livingFile, kitchenFile);
        undone.Changes.HeroChanged.ShouldBeFalse();
        undone.HasUnpublishedChanges.ShouldBeFalse();

        // taking one off is a draft change: clients keep it until the next publish
        var removed = await ChangeAsync(await Client.DeleteAsync($"{Base(s.Project.Id)}/heroes/{kitchenFile}", s.Staff.AccessToken));
        removed.Heroes.Select(h => h.FileId).ShouldBe([coverFile, livingFile]);
        removed.Items.Single(i => i.Id == kitchen.Item.Id).IsHero.ShouldBeFalse();
        removed.Changes.HeroChanged.ShouldBeTrue();
        await (await Client.DeleteAsync($"{Base(s.Project.Id)}/heroes/{kitchenFile}", s.Staff.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Heroes.Count.ShouldBe(3);
        await PublishAsync(s.Staff, s.Project.Id);
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Heroes.Select(h => h.Name).ShouldBe(["Cover.jpg", "Living room.jpg"]);

        // the kitchen is still one of the project's files
        (await LiveFilesAsync(kitchenFile)).ShouldBe(1);
    }

    [Fact]
    public async Task A_hero_image_keeps_its_file_until_neither_list_uses_it()
    {
        var s = await ProjectWithClientAsync();
        var living = await UploadAsync(s.Staff, s.Project.Id, "Living room.jpg", Jpeg);
        var livingFile = living.Item!.File.Id;
        await AddHeroAsync(s.Staff, s.Project.Id, living.Item.Id);

        // deleting the item doesn't take the hero image with it: the hero keeps the file
        (await Client.DeleteAsync($"{Files(s.Project.Id)}/items/{living.Item.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await LiveFilesAsync(livingFile)).ShouldBe(1);
        (await PreviewAsync(s.Staff, s.Project.Id)).Heroes.Single().Name.ShouldBe("Living room.jpg");
        (await ContentAsync(s.Staff, s.Project.Id)).Hero!.FileId.ShouldBe(livingFile);
        await UploadAsync(s.Staff, s.Project.Id, "Cover.jpg", OtherJpeg, hero: true);
        await PublishAsync(s.Staff, s.Project.Id);

        // off the draft, the published list still holds it
        await ChangeAsync(await Client.DeleteAsync($"{Base(s.Project.Id)}/heroes/{livingFile}", s.Staff.AccessToken));
        (await LiveFilesAsync(livingFile)).ShouldBe(1);
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Heroes.Select(h => h.Name).ShouldBe(["Living room.jpg", "Cover.jpg"]);

        // published without it: nothing uses it any more, so it goes
        await PublishAsync(s.Staff, s.Project.Id);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_files WHERE id = @id", ("@id", livingFile))).ShouldBe(0);
        (await ClientViewAsync(s.ClientToken, s.Project.Id)).Heroes.Select(h => h.Name).ShouldBe(["Cover.jpg"]);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_hero_images WHERE project_id = @p", ("@p", s.Project.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task Discarding_puts_the_published_hero_images_back_and_lets_a_draft_only_upload_go()
    {
        var s = await ProjectWithClientAsync();
        var a = await UploadAsync(s.Staff, s.Project.Id, "A.jpg", Jpeg);
        var b = await UploadAsync(s.Staff, s.Project.Id, "B.jpg", OtherJpeg);
        await AddHeroAsync(s.Staff, s.Project.Id, a.Item!.Id);
        await PublishAsync(s.Staff, s.Project.Id);

        await AddHeroAsync(s.Staff, s.Project.Id, b.Item!.Id);
        await UploadAsync(s.Staff, s.Project.Id, "Cover.jpg", OtherJpeg, hero: true);
        var draft = await ContentAsync(s.Staff, s.Project.Id);
        var coverFile = draft.Heroes.Single(h => h.Name == "Cover.jpg").FileId;
        draft = await OrderHeroesAsync(s.Staff, s.Project.Id, coverFile, b.Item.File.Id, a.Item.File.Id);
        draft.Changes.HeroChanged.ShouldBeTrue();

        var discarded = await ChangeAsync(await Client.PostJsonAsync($"{Base(s.Project.Id)}/discard", new { expectedRevision = draft.Revision }, s.Staff.AccessToken));
        discarded.Heroes.Select(h => h.FileId).ShouldBe([a.Item.File.Id]);
        discarded.Changes.HeroChanged.ShouldBeFalse();
        discarded.HasUnpublishedChanges.ShouldBeFalse();

        // B is still one of the project's files; the uploaded cover was only ever a draft hero image, so it goes
        (await LiveFilesAsync(b.Item.File.Id)).ShouldBe(1);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_files WHERE id = @id", ("@id", coverFile))).ShouldBe(0);
    }

    [Fact]
    public async Task There_are_at_most_twelve_hero_images()
    {
        var s = await ProjectWithClientAsync();
        var extra = await UploadAsync(s.Staff, s.Project.Id, "Extra.jpg", Jpeg);
        for (var i = 1; i <= 12; i++)
        {
            await UploadAsync(s.Staff, s.Project.Id, $"Cover {i}.jpg", OtherJpeg, hero: true);
        }

        var full = await ContentAsync(s.Staff, s.Project.Id);
        full.Heroes.Count.ShouldBe(12);
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = extra.Item!.Id }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalHeroLimit);
        await (await Client.PostJsonAsync($"{Files(s.Project.Id)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), hero = true, fileName = "One more.jpg", size = Jpeg.Length }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalHeroLimit);

        // one off, and there's room again
        await ChangeAsync(await Client.DeleteAsync($"{Base(s.Project.Id)}/heroes/{full.Heroes[0].FileId}", s.Staff.AccessToken));
        (await AddHeroAsync(s.Staff, s.Project.Id, extra.Item.Id)).Heroes[^1].Name.ShouldBe("Extra.jpg");
    }

    [Fact]
    public async Task The_clients_project_list_shows_each_projects_cover_dates_and_news_newest_project_first()
    {
        var s = await ProjectWithClientAsync();
        async Task<IReadOnlyList<PortalProjectResponse>> ListAsync()
        {
            var me = await AsClientAsync(s.ClientToken, "/api/portal/me");
            me.IsSuccessStatusCode.ShouldBeTrue(await me.Content.ReadAsStringAsync());
            return (await me.ReadAsync<PortalAccountResponse>()).Projects;
        }

        async Task<PortalProjectResponse> CardAsync() => (await ListAsync()).Single(p => p.Id == s.Project.Id);

        // nothing published or written yet: nothing new, no cover, no activity
        var fresh = await CardAsync();
        fresh.StartedOn.ShouldBe(s.Project.StartDate);
        fresh.HasNews.ShouldBeFalse();
        fresh.HeroUrl.ShouldBeNull();
        fresh.HeroUrlExpiresAt.ShouldBeNull();
        fresh.LastPublishedAt.ShouldBeNull();
        fresh.LastActivityAt.ShouldBeNull();

        // published and never opened: news, with the first published hero image as the cover
        var living = await UploadAsync(s.Staff, s.Project.Id, "Living room.jpg", Jpeg);
        await AddHeroAsync(s.Staff, s.Project.Id, living.Item!.Id);
        await UploadAsync(s.Staff, s.Project.Id, "Cover.jpg", OtherJpeg, hero: true);
        var first = await PublishAsync(s.Staff, s.Project.Id);
        var published = await CardAsync();
        published.HasNews.ShouldBeTrue();
        published.Unread.ShouldBe(0);
        published.LastPublishedAt.ShouldBe(first.Publication.PublishedAt);
        published.LastActivityAt.ShouldBe(first.Publication.PublishedAt);
        published.HeroUrlExpiresAt.ShouldNotBeNull();
        (await (await Client.GetAsync(published.HeroUrl!)).Content.ReadAsByteArrayAsync()).ShouldBe(Jpeg);

        // opened: no longer news, until the next publish
        await ClientViewAsync(s.ClientToken, s.Project.Id);
        (await CardAsync()).HasNews.ShouldBeFalse();
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Welcome" }, s.Staff.AccessToken));
        var second = await PublishAsync(s.Staff, s.Project.Id);
        var republished = await CardAsync();
        republished.HasNews.ShouldBeTrue();
        republished.LastPublishedAt.ShouldBe(second.Publication.PublishedAt);
        await ClientViewAsync(s.ClientToken, s.Project.Id);
        (await CardAsync()).HasNews.ShouldBeFalse();

        // a team message is news while unread, and the latest activity
        var sent = await Client.PostJsonAsync($"/api/projects/{s.Project.Id}/portal/discussion/messages",
            new { body = "Have a look", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }, s.Staff.AccessToken);
        sent.IsSuccessStatusCode.ShouldBeTrue(await sent.Content.ReadAsStringAsync());
        var message = await sent.ReadAsync<PortalChatMessageResponse>();
        var written = await CardAsync();
        written.Unread.ShouldBe(1);
        written.HasNews.ShouldBeTrue();
        written.LastActivityAt.ShouldBe(message.CreatedAt);
        written.LastPublishedAt.ShouldBe(second.Publication.PublishedAt);

        // the newest project first: by start date, then id
        var other = await ProjectAsync(s.Staff, DomainCodes.Design, s.ClientId);
        await GrantAsync(s.Staff, other.Id, s.ContactEmail, firstTime: false);
        await _steps.ExecuteAsync("UPDATE projects SET start_date = '2026-01-10' WHERE id = @p", ("@p", s.Project.Id));
        var list = await ListAsync();
        list.Select(p => p.Id).ShouldBe([other.Id, s.Project.Id]);
        list[1].StartedOn.ShouldBe(new DateOnly(2026, 1, 10));
        list[0].HasNews.ShouldBeFalse();
        list[0].LastPublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_removed_file_stays_for_clients_until_the_next_publish_and_can_be_undone()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        await PublishAsync(s.Staff, s.Project.Id);

        (await Client.DeleteAsync($"{Files(s.Project.Id)}/items/{kitchen.Item!.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        Titles(await ClientViewAsync(s.ClientToken, s.Project.Id), PortalSection.Presentation).ShouldBe(["Kitchen"]);
        Titles(await PreviewAsync(s.Staff, s.Project.Id), PortalSection.Presentation).ShouldBeEmpty();

        var restored = await Client.PostEmptyAsync($"{Files(s.Project.Id)}/items/{kitchen.Item.Id}/restore", s.Staff.AccessToken);
        restored.IsSuccessStatusCode.ShouldBeTrue(await restored.Content.ReadAsStringAsync());
        (await restored.ReadAsync<ProjectFilesResponse>()).Items.Single().Removed.ShouldBeFalse();
        var content = await ContentAsync(s.Staff, s.Project.Id);
        content.Items.Single().State.ShouldBe(PortalItemState.Unchanged);
        content.HasUnpublishedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Discarding_puts_back_the_client_choices_hero_and_introduction_but_not_the_files_themselves()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        var revision = (await ContentAsync(s.Staff, s.Project.Id)).Revision;
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/discard", new { expectedRevision = revision }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalNeverPublished);

        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Welcome to your villa." }, s.Staff.AccessToken));
        await PublishAsync(s.Staff, s.Project.Id);

        await UploadAsync(s.Staff, s.Project.Id, "Kitchen v2.jpg", OtherJpeg, replaceItemId: kitchen.Item!.Id);
        await OkAsync(await Client.PutJsonAsync($"{Files(s.Project.Id)}/items/{kitchen.Item.Id}", new { title = "Renamed", section = "model" }, s.Staff.AccessToken));
        var extra = await UploadAsync(s.Staff, s.Project.Id, "Extra.jpg", Jpeg);
        await ChangeAsync(await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Something else" }, s.Staff.AccessToken));

        var draft = await ContentAsync(s.Staff, s.Project.Id);
        draft.CanDiscard.ShouldBeTrue();
        var discarded = await ChangeAsync(await Client.PostJsonAsync($"{Base(s.Project.Id)}/discard", new { expectedRevision = draft.Revision }, s.Staff.AccessToken));

        // the introduction and the choices are back; the extra file is the team's again, still in Project files
        discarded.Summary.ShouldBe("Welcome to your villa.");
        discarded.Items.Select(i => i.Id).ShouldBe([kitchen.Item.Id]);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_items WHERE id = @id AND for_client = 0", ("@id", extra.Item!.Id))).ShouldBe(1);

        // the kitchen's new file, title and section belong to Project files: they stay, still waiting to be published
        var item = discarded.Items.Single();
        item.Title.ShouldBe("Renamed");
        item.Section.ShouldBe(PortalSection.Model);
        item.File.Id.ShouldNotBe(kitchen.Item.File.Id);
        item.State.ShouldBe(PortalItemState.Changed);
        discarded.HasUnpublishedChanges.ShouldBeTrue();
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
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = content.Items[0].Id }, s.Staff.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalContentReadOnly);
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order", new { itemIds = content.Items.Select(i => i.Id) }, s.Staff.AccessToken))
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
    public async Task Admins_look_at_the_client_portal_but_only_client_portal_managers_arrange_or_publish_it()
    {
        var s = await ProjectWithClientAsync();
        var kitchen = await UploadAsync(s.Staff, s.Project.Id, "Kitchen.jpg", Jpeg);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Design);

        foreach (var outsider in new[] { employee, executionAdmin })
        {
            await (await Client.GetAsync(Base(s.Project.Id), outsider.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/publish", new { expectedRevision = 0 }, outsider.AccessToken))
                .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.GetAsync($"{Base(s.Project.Id)}/preview", outsider.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = kitchen.Item!.Id }, outsider.AccessToken))
                .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }

        // a Design admin manages client access, so sees the portal and its preview, read-only
        designAdmin.User.Permissions.ShouldContain(PermissionCodes.ClientAccessManage);
        designAdmin.User.Permissions.ShouldNotContain(PermissionCodes.ClientPortalManage);
        var seen = await ContentAsync(designAdmin, s.Project.Id);
        seen.CanManage.ShouldBeFalse();
        seen.CanEdit.ShouldBeFalse();
        seen.CanPublish.ShouldBeFalse();
        seen.Items.Single().Id.ShouldBe(kitchen.Item!.Id);
        Titles(await PreviewAsync(designAdmin, s.Project.Id), PortalSection.Presentation).ShouldBe(["Kitchen"]);
        (await Client.GetAsync($"{Base(s.Project.Id)}/items/{kitchen.Item.Id}/link", designAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/publish", new { expectedRevision = seen.Revision }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/summary", new { summary = "Hello" }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/heroes", new { itemId = kitchen.Item.Id }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // granted client_portal:manage, they may: the same rule as for a super admin
        await OkAsync(await Client.PutJsonAsync($"/api/users/{designAdmin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ClientPortalManage, action = "grant", reason = "Publishes for this client" } } },
            s.Staff.AccessToken));
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
