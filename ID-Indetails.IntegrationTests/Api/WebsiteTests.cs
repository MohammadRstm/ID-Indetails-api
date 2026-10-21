using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The Website page and the public website API (docs/website.md).</summary>
public class WebsiteTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Public = "/api/public/website";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    /// <summary>A real photo: the website takes only pictures the server can make copies of.</summary>
    private async Task<long> PngAsync(AuthResponse who) => (await UploadAsync(who, FileTests.Photo, "plan.png")).Id;

    private async Task<ID_Indetails.api.DTO.Response.Files.FileResponse> UploadAsync(AuthResponse who, byte[] bytes, string name)
    {
        var response = await FileTests.UploadAsync(Client, bytes, name, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ID_Indetails.api.DTO.Response.Files.FileResponse>();
    }

    private static string Unique(string title) => $"{title} {Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>A project with everything the website shows.</summary>
    private static object Full(
        string title, long? thumbnail, long? hero, bool published = true, long? projectId = null, string? status = "completed",
        int? year = 2022)
        => new
        {
            title,
            type = "villa",
            status,
            year,
            client = "The Rizk family",
            city = "Beirut",
            country = "Lebanon",
            latitude = 33.893800m,
            longitude = 35.501800m,
            description = "A family villa above the sea.",
            projectId,
            thumbnailFileId = thumbnail,
            heroFileId = hero,
            heroAlt = "The villa at dusk",
            published,
        };

    private async Task<WebsiteProjectResponse> CreateAsync(AuthResponse who, object body)
    {
        var response = await Client.PostJsonAsync("/api/website/projects", body, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WebsiteProjectResponse>();
    }

    private async Task<WebsiteProjectResponse> PublishedAsync(AuthResponse who, string title)
        => await CreateAsync(who, Full(Unique(title), await PngAsync(who), await PngAsync(who)));

    private async Task<WebsiteProjectResponse> UpdateAsync(AuthResponse who, long id, object body)
    {
        var response = await Client.PutJsonAsync($"/api/website/projects/{id}", body, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WebsiteProjectResponse>();
    }

    private async Task<IReadOnlyList<PublicWebsiteProject>> PublicProjectsAsync()
        => await (await Client.GetAsync($"{Public}/projects")).ReadAsync<List<PublicWebsiteProject>>();

    [Fact]
    public async Task Only_super_admins_see_and_change_the_website_until_someone_is_granted_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.GetAsync("/api/website/projects")).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.GetAsync("/api/website/projects", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/website/content", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/website/services", new { title = "Interiors", description = "Whole homes." }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync("/api/company/contact-email", new { email = "hello@example.com" }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var list = await (await Client.GetAsync("/api/website/projects", superAdmin.AccessToken)).ReadAsync<WebsiteProjectsResponse>();
        list.CanManage.ShouldBeTrue();
        list.ConveyorLimit.ShouldBe(20);

        // looking, granted on its own, doesn't let them change anything
        var grant = await Client.PutJsonAsync(
            $"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WebsiteView, action = "grant", reason = "Integration test" } } },
            superAdmin.AccessToken);
        grant.IsSuccessStatusCode.ShouldBeTrue(await grant.Content.ReadAsStringAsync());
        var seen = await (await Client.GetAsync("/api/website/projects", admin.AccessToken)).ReadAsync<WebsiteProjectsResponse>();
        seen.CanManage.ShouldBeFalse();
        (await (await Client.GetAsync("/api/website/content", admin.AccessToken)).ReadAsync<WebsiteContentResponse>()).CanManage.ShouldBeFalse();
        await (await Client.PostJsonAsync("/api/website/projects", Full(Unique("Villa"), null, null, published: false), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_draft_is_published_once_it_has_everything_and_the_public_site_reads_only_published_projects()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var title = Unique("Villa Rizk");

        var draft = await CreateAsync(superAdmin, new { title, type = "hotel", status = "in_progress", year = 2026, published = false });
        draft.Slug.ShouldBe(WebsiteSlug(title));
        draft.Published.ShouldBeFalse();
        draft.MissingToPublish.ShouldContain("a card picture");
        draft.MissingToPublish.ShouldContain("a position on the map");

        await (await Client.PutJsonAsync($"/api/website/projects/{draft.Id}", new { title, type = "hotel", status = "in_progress", year = 2026, published = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WebsiteProjectIncomplete);
        (await PublicProjectsAsync()).ShouldNotContain(p => p.Id == draft.Id);
        await (await Client.GetAsync($"{Public}/projects/{draft.Slug}")).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var thumbnail = await PngAsync(superAdmin);
        var hero = await PngAsync(superAdmin);
        var published = await UpdateAsync(superAdmin, draft.Id, Full(title, thumbnail, hero));
        published.Published.ShouldBeTrue();
        published.MissingToPublish.ShouldBeEmpty();
        published.Slug.ShouldBe(draft.Slug);

        var shown = (await PublicProjectsAsync()).Single(p => p.Id == draft.Id);
        shown.Slug.ShouldBe(draft.Slug);
        shown.Type.ShouldBe("villa");
        shown.Status.ShouldBe("completed");
        shown.Year.ShouldBe(2022);
        shown.Client.ShouldBe("The Rizk family");
        shown.Location.ShouldBe(new PublicPlace("Beirut", "Lebanon", "LB", 33.893800m, 35.501800m, false));
        shown.Thumbnail.ShouldBe($"{Public}/files/{thumbnail}");
        shown.Hero.Src.ShouldBe($"{Public}/files/{hero}");
        shown.Hero.Alt.ShouldBe("The villa at dusk");
        shown.ShowInConveyor.ShouldBeFalse();
        (await (await Client.GetAsync($"{Public}/projects/{draft.Slug}")).ReadAsync<PublicWebsiteProject>()).Id.ShouldBe(draft.Id);

        // the address comes from the name: the same name is numbered, a rename moves it, and a name needs Latin letters
        var twin = await CreateAsync(superAdmin, Full(title, null, null, published: false));
        twin.Slug.ShouldBe($"{draft.Slug}-2");
        var renamed = await UpdateAsync(superAdmin, twin.Id, Full(title + " Annex", null, null, published: false));
        renamed.Slug.ShouldBe(WebsiteSlug(title + " Annex"));
        (await (await Client.PostJsonAsync("/api/website/projects", Full("فيلا", null, null, published: false), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("title");

        // taken off the website
        await UpdateAsync(superAdmin, draft.Id, Full(title, thumbnail, hero, published: false));
        (await PublicProjectsAsync()).ShouldNotContain(p => p.Id == draft.Id);
        (await Client.DeleteAsync($"/api/website/projects/{draft.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.GetAsync($"/api/website/projects/{draft.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_linked_project_follows_the_system_status_and_year_until_they_are_typed_in()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var candidates = await (await Client.GetAsync($"/api/website/link-candidates?search={project.Title}", superAdmin.AccessToken))
            .ReadAsync<List<WebsiteLinkCandidateResponse>>();
        var candidate = candidates.Single(c => c.Id == project.Id);
        candidate.Status.ShouldBe(WebsiteProjectStatus.InProgress);
        candidate.WebsiteProjectId.ShouldBeNull();

        var title = Unique("Linked villa");
        var linked = await CreateAsync(superAdmin, Full(title, null, null, published: false, projectId: project.Id, status: null, year: null));
        linked.Status.ShouldBeNull();
        linked.ShownStatus.ShouldBe(WebsiteProjectStatus.InProgress);
        linked.LinkedProject!.Id.ShouldBe(project.Id);
        linked.ShownYear.ShouldBe(candidate.Year);

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var followed = await (await Client.GetAsync($"/api/website/projects/{linked.Id}", superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();
        followed.ShownStatus.ShouldBe(WebsiteProjectStatus.Completed);
        followed.ShownYear.ShouldBe(DateTime.UtcNow.Year);

        // typed in, it stays; cleared, it follows again
        var typed = await UpdateAsync(superAdmin, linked.Id, Full(title, null, null, published: false, projectId: project.Id, status: "in_progress", year: 2019));
        (typed.ShownStatus, typed.ShownYear).ShouldBe((WebsiteProjectStatus.InProgress, 2019));
        var cleared = await UpdateAsync(superAdmin, linked.Id, Full(title, null, null, published: false, projectId: project.Id, status: null, year: null));
        (cleared.ShownStatus, cleared.ShownYear).ShouldBe((WebsiteProjectStatus.Completed, DateTime.UtcNow.Year));

        // a system project is shown once, and an unlinked project carries its own status and year
        await (await Client.PostJsonAsync("/api/website/projects", Full(Unique("Twin"), null, null, published: false, projectId: project.Id), superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.WebsiteProjectAlreadyLinked);
        await (await Client.PostJsonAsync("/api/website/projects", Full(Unique("Loose"), null, null, published: false, status: null), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await (await Client.GetAsync($"/api/website/link-candidates?search={project.Title}", superAdmin.AccessToken))
            .ReadAsync<List<WebsiteLinkCandidateResponse>>()).Single(c => c.Id == project.Id).WebsiteProjectId.ShouldBe(linked.Id);
    }

    [Fact]
    public async Task The_moving_strip_holds_up_to_twenty_published_projects_in_order()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var first = await PublishedAsync(superAdmin, "First");
        var second = await PublishedAsync(superAdmin, "Second");
        var draft = await CreateAsync(superAdmin, Full(Unique("Draft"), null, null, published: false));

        var strip = await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = new[] { second.Id, first.Id } }, superAdmin.AccessToken);
        ((int)strip.StatusCode).ShouldBe(200, await strip.Content.ReadAsStringAsync());
        var shown = (await PublicProjectsAsync()).Where(p => p.ShowInConveyor).OrderBy(p => p.ConveyorOrder).Select(p => p.Id).ToList();
        shown.ShouldBe([second.Id, first.Id]);

        await (await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = new[] { draft.Id } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WebsiteConveyorInvalid);
        await (await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = Enumerable.Range(1, 21).Select(i => (long)i) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // taken off the website, a project leaves the strip and the gap closes
        await UpdateAsync(superAdmin, second.Id, Full(second.Title, second.ThumbnailFileId, second.HeroFileId, published: false));
        var after = await (await Client.GetAsync($"/api/website/projects/{first.Id}", superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();
        after.ConveyorOrder.ShouldBe(1);

        var cleared = await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = Array.Empty<long>() }, superAdmin.AccessToken);
        (await cleared.ReadAsync<WebsiteProjectsResponse>()).Projects.ShouldAllBe(p => p.ConveyorOrder == null);
    }

    [Fact]
    public async Task A_gallery_is_added_described_ordered_and_removed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await PublishedAsync(superAdmin, "Gallery villa");

        var withOne = await Client.PostJsonAsync($"/api/website/projects/{project.Id}/images", new { fileId = await PngAsync(superAdmin), alt = "The pool" }, superAdmin.AccessToken);
        ((int)withOne.StatusCode).ShouldBe(200, await withOne.Content.ReadAsStringAsync());
        var withTwo = await (await Client.PostJsonAsync(
            $"/api/website/projects/{project.Id}/images",
            new { fileId = await PngAsync(superAdmin), alt = "The terrace" },
            superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();
        var (pool, terrace) = (withTwo.Gallery[0], withTwo.Gallery[1]);

        var ordered = await (await Client.PutJsonAsync($"/api/website/projects/{project.Id}/images/order", new { ids = new[] { terrace.Id, pool.Id } }, superAdmin.AccessToken))
            .ReadAsync<WebsiteProjectResponse>();
        ordered.Gallery.Select(i => i.Id).ShouldBe([terrace.Id, pool.Id]);
        await (await Client.PutJsonAsync($"/api/website/projects/{project.Id}/images/order", new { ids = new[] { pool.Id } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.WebsiteOrderStale);

        await Client.PutJsonAsync($"/api/website/projects/{project.Id}/images/{pool.Id}", new { alt = "The pool at noon" }, superAdmin.AccessToken);
        var gallery = (await PublicProjectsAsync()).Single(p => p.Id == project.Id).Gallery;
        gallery.Select(i => i.Alt).ShouldBe(["The terrace", "The pool at noon"]);
        gallery[0].Src.ShouldBe($"{Public}/files/{terrace.FileId}");

        var removed = await (await Client.DeleteAsync($"/api/website/projects/{project.Id}/images/{terrace.Id}", superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();
        removed.Gallery.Select(i => i.Id).ShouldBe([pool.Id]);
    }

    [Fact]
    public async Task A_project_has_a_concept_up_to_five_pictures_and_a_text_kept_apart_from_its_gallery()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await PublishedAsync(superAdmin, "Concept villa");
        await UpdateAsync(superAdmin, project.Id, new
        {
            title = project.Title, type = "villa", status = "completed", year = 2022, client = "The Rizk family", city = "Beirut", country = "Lebanon",
            latitude = 33.893800m, longitude = 35.501800m, description = "A family villa above the sea.",
            conceptDescription = "Light first: every room faces the sea.", thumbnailFileId = project.ThumbnailFileId, heroFileId = project.HeroFileId,
            heroAlt = "The villa at dusk", published = true,
        });
        var gallery = await (await Client.PostJsonAsync($"/api/website/projects/{project.Id}/images", new { fileId = await PngAsync(superAdmin), alt = "The pool" }, superAdmin.AccessToken))
            .ReadAsync<WebsiteProjectResponse>();

        WebsiteProjectResponse current = gallery;
        foreach (var sketch in new[] { "Sketch 1", "Sketch 2", "Sketch 3", "Sketch 4", "Sketch 5" })
        {
            var added = await Client.PostJsonAsync($"/api/website/projects/{project.Id}/images", new { fileId = await PngAsync(superAdmin), alt = sketch, concept = true }, superAdmin.AccessToken);
            ((int)added.StatusCode).ShouldBe(200, await added.Content.ReadAsStringAsync());
            current = await added.ReadAsync<WebsiteProjectResponse>();
        }

        current.ConceptImages.Select(i => i.Alt).ShouldBe(["Sketch 1", "Sketch 2", "Sketch 3", "Sketch 4", "Sketch 5"]);
        current.Gallery.ShouldHaveSingleItem().Alt.ShouldBe("The pool");
        current.ConceptDescription.ShouldBe("Light first: every room faces the sea.");
        await (await Client.PostJsonAsync($"/api/website/projects/{project.Id}/images", new { fileId = await PngAsync(superAdmin), alt = "Sketch 6", concept = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WebsiteConceptFull);

        // ordered apart from the gallery: the last sketch becomes the main one
        var ids = current.ConceptImages.Select(i => i.Id).ToList();
        var ordered = await (await Client.PutJsonAsync($"/api/website/projects/{project.Id}/images/order", new { ids = ids.Skip(4).Concat(ids.Take(4)) }, superAdmin.AccessToken))
            .ReadAsync<WebsiteProjectResponse>();
        ordered.ConceptImages[0].Alt.ShouldBe("Sketch 5");

        var shown = (await PublicProjectsAsync()).Single(p => p.Id == project.Id);
        shown.ConceptDescription.ShouldBe("Light first: every room faces the sea.");
        shown.ConceptImages.Select(i => i.Alt).ShouldBe(["Sketch 5", "Sketch 1", "Sketch 2", "Sketch 3", "Sketch 4"]);
        shown.ConceptImages[0].Src.ShouldBe($"{Public}/files/{ordered.ConceptImages[0].FileId}");
        shown.Gallery.ShouldHaveSingleItem().Alt.ShouldBe("The pool");
        (await Client.GetAsync($"{Public}/files/{ordered.ConceptImages[0].FileId}")).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_public_site_serves_only_its_own_pictures()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var published = await PublishedAsync(superAdmin, "Pictured");
        var draft = await CreateAsync(superAdmin, Full(Unique("Hidden"), await PngAsync(superAdmin), null, published: false));
        var loose = await PngAsync(superAdmin);

        var picture = await Client.GetAsync($"{Public}/files/{published.ThumbnailFileId}");
        ((int)picture.StatusCode).ShouldBe(200);
        // never the file as it was uploaded: always a new copy
        picture.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        (await picture.Content.ReadAsByteArrayAsync()).ShouldNotBe(FileTests.Photo);
        picture.Headers.CacheControl!.Public.ShouldBeTrue();

        await (await Client.GetAsync($"{Public}/files/{draft.ThumbnailFileId}")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"{Public}/files/{loose}")).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // the favicon too, once chosen
        var favicon = await PngAsync(superAdmin);
        await (await Client.GetAsync($"{Public}/files/{favicon}")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var set = await Client.PutJsonAsync("/api/website/favicon", new { fileId = favicon }, superAdmin.AccessToken);
        ((int)set.StatusCode).ShouldBe(200, await set.Content.ReadAsStringAsync());
        (await (await Client.GetAsync($"{Public}/settings")).ReadAsync<PublicWebsiteSettings>()).FaviconUrl.ShouldBe($"{Public}/files/{favicon}");
        ((int)(await Client.GetAsync($"{Public}/files/{favicon}")).StatusCode).ShouldBe(200);
    }

    [Fact]
    public async Task A_picture_the_server_cant_copy_is_never_put_on_the_website_nor_served_as_it_was_uploaded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await PublishedAsync(superAdmin, "Unreadable");

        // an iPhone photo (HEIC) can't be read here, so no copy of it could be made: it is refused for the website
        var heic = await UploadAsync(superAdmin, FileTests.Heic, "IMG_0001.HEIC");
        heic.MimeType.ShouldBe("image/heic");
        await (await Client.PostJsonAsync($"/api/website/projects/{project.Id}/images", new { fileId = heic.Id, alt = "The pool" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
        await (await Client.PutJsonAsync("/api/website/favicon", new { fileId = heic.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);

        // one put there before this rule (straight in the database here) is not found, never sent as it was uploaded
        await _steps.ExecuteAsync("UPDATE website_projects SET thumbnail_file_id = @f WHERE id = @p", ("@f", heic.Id), ("@p", project.Id));
        await (await Client.GetAsync($"{Public}/files/{heic.Id}")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"{Public}/files/{heic.Id}?size=160")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Services_locations_and_social_links_are_kept_in_order()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;

        var design = Unique("Architecture");
        var interiors = Unique("Interiors");
        await Client.PostJsonAsync("/api/website/services", new { title = design, description = "Buildings from the first sketch." }, token);
        var content = await (await Client.PostJsonAsync("/api/website/services", new { title = interiors, description = "Whole homes." }, token)).ReadAsync<WebsiteContentResponse>();
        var ids = content.Services.Where(s => s.Title == design || s.Title == interiors).Select(s => s.Id).ToList();
        ids.Count.ShouldBe(2);

        var all = content.Services.Select(s => s.Id).ToList();
        var reversed = all.AsEnumerable().Reverse().ToList();
        var ordered = await (await Client.PutJsonAsync("/api/website/services/order", new { ids = reversed }, token)).ReadAsync<WebsiteContentResponse>();
        ordered.Services.Select(s => s.Id).ShouldBe(reversed);
        (await (await Client.GetAsync($"{Public}/services")).ReadAsync<List<PublicWebsiteOffering>>()).Select(s => s.Id).ShouldBe(reversed);

        var address = Unique("Gemmayze, Beirut");
        var located = await (await Client.PostJsonAsync(
            "/api/website/locations",
            new { city = "Beirut", country = "Lebanon", latitude = 33.8938m, longitude = 35.5018m, address, phone = "+961 1 234 567" },
            token)).ReadAsync<WebsiteContentResponse>();
        var office = located.Locations.Single(l => l.Address == address);
        (await (await Client.GetAsync($"{Public}/locations")).ReadAsync<List<PublicWebsiteLocation>>()).ShouldContain(l => l.Id == office.Id && l.Lat == 33.8938m);
        await (await Client.PostJsonAsync("/api/website/locations", new { city = "Beirut", country = "Lebanon", latitude = 95m, longitude = 0m, address = "x", phone = "1" }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var label = Unique("Instagram");
        var linked = await (await Client.PostJsonAsync("/api/website/social-links", new { label, url = "https://instagram.com/indetails" }, token)).ReadAsync<WebsiteContentResponse>();
        var link = linked.SocialLinks.Single(l => l.Label == label);
        await (await Client.PostJsonAsync("/api/website/social-links", new { label, url = "http://instagram.com/indetails" }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await Client.PutJsonAsync($"/api/website/social-links/{link.Id}", new { label, url = "https://instagram.com/id.indetails" }, token);
        (await (await Client.GetAsync($"{Public}/social-links")).ReadAsync<List<PublicWebsiteSocialLink>>())
            .ShouldContain(l => l.Id == link.Id && l.Url == "https://instagram.com/id.indetails");

        var gone = await (await Client.DeleteAsync($"/api/website/social-links/{link.Id}", token)).ReadAsync<WebsiteContentResponse>();
        gone.SocialLinks.ShouldNotContain(l => l.Id == link.Id);
        await (await Client.DeleteAsync($"/api/website/social-links/{link.Id}", token)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task The_contact_form_emails_the_company_address_set_in_settings_and_drops_robots()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var message = new { name = "Nadine <b>Haddad</b>", email = "nadine@example.com", phone = "+961 70 123 456", message = "We'd like a villa in Batroun.", website = "" };

        (await Client.PutJsonAsync("/api/company/contact-email", new { email = "" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"{Public}/contact-messages", message)).ShouldFailWithAsync(503, ErrorCodes.WebsiteContactUnavailable);

        var address = AuthTestSteps.NewEmail("studio");
        var settings = await (await Client.PutJsonAsync("/api/company/contact-email", new { email = address }, superAdmin.AccessToken)).ReadAsync<CompanySettingsResponse>();
        settings.ContactEmail.ShouldBe(address);
        (await (await Client.GetAsync($"{Public}/contact")).ReadAsync<PublicWebsiteContact>()).Email.ShouldBe(address);
        (await (await Client.GetAsync("/api/website/content", superAdmin.AccessToken)).ReadAsync<WebsiteContentResponse>()).ContactEmail.ShouldBe(address);

        // a robot fills the hidden field: answered the same, sent nowhere
        (await Client.PostJsonAsync($"{Public}/contact-messages", message with { website = "http://spam.example" })).StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        api.Emails.CountTo(address).ShouldBe(0);

        var invalid = await (await Client.PostJsonAsync($"{Public}/contact-messages", message with { message = "Hi", phone = "12" }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        invalid.Errors!.Keys.ShouldBe(["phone", "message"], ignoreOrder: true);

        var sent = await Client.PostJsonAsync($"{Public}/contact-messages", message);
        ((int)sent.StatusCode).ShouldBe(202, await sent.Content.ReadAsStringAsync());
        (await sent.Content.ReadAsStringAsync()).ShouldNotContain("Batroun");
        var email = api.Emails.LastTo(address);
        email.ReplyToAddress.ShouldBe("nadine@example.com");
        email.HtmlBody.ShouldContain("Nadine &lt;b&gt;Haddad&lt;/b&gt;");
        email.HtmlBody.ShouldNotContain("<b>Haddad</b>");
        email.TextBody.ShouldContain("We'd like a villa in Batroun.");
    }

    [Fact]
    public async Task The_website_goes_under_construction_and_back_with_its_contact_form_still_open()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, viewer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var grant = await Client.PutJsonAsync($"/api/users/{viewer.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WebsiteView, action = "grant", reason = "Integration test" } } }, superAdmin.AccessToken);
        grant.IsSuccessStatusCode.ShouldBeTrue(await grant.Content.ReadAsStringAsync());
        async Task<PublicWebsiteSettings> PublicSettingsAsync() => await (await Client.GetAsync($"{Public}/settings")).ReadAsync<PublicWebsiteSettings>();

        (await PublicSettingsAsync()).UnderConstruction.ShouldBeFalse();
        // looking at the website doesn't let them take it down
        await (await Client.PutJsonAsync("/api/website/under-construction", new { underConstruction = true }, viewer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var down = await (await Client.PutJsonAsync("/api/website/under-construction", new { underConstruction = true }, superAdmin.AccessToken))
            .ReadAsync<WebsiteContentResponse>();
        down.UnderConstructionSince.ShouldNotBeNull();
        (await PublicSettingsAsync()).UnderConstruction.ShouldBeTrue();
        // switching it on again keeps the day it started
        (await (await Client.PutJsonAsync("/api/website/under-construction", new { underConstruction = true }, superAdmin.AccessToken))
            .ReadAsync<WebsiteContentResponse>()).UnderConstructionSince.ShouldBe(down.UnderConstructionSince);
        (await (await Client.GetAsync("/api/website/content", viewer.AccessToken)).ReadAsync<WebsiteContentResponse>()).UnderConstructionSince
            .ShouldBe(down.UnderConstructionSince);

        // the construction page sends visitors to the contact form, which keeps working
        var address = AuthTestSteps.NewEmail("studio");
        (await Client.PutJsonAsync("/api/company/contact-email", new { email = address }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var sent = await Client.PostJsonAsync($"{Public}/contact-messages",
            new { name = "Nadine Haddad", email = "nadine@example.com", phone = (string?)null, message = "When is the website back?", website = "" });
        ((int)sent.StatusCode).ShouldBe(202, await sent.Content.ReadAsStringAsync());
        api.Emails.CountTo(address).ShouldBe(1);

        var back = await (await Client.PutJsonAsync("/api/website/under-construction", new { underConstruction = false }, superAdmin.AccessToken))
            .ReadAsync<WebsiteContentResponse>();
        back.UnderConstructionSince.ShouldBeNull();
        (await PublicSettingsAsync()).UnderConstruction.ShouldBeFalse();
    }

    /// <summary>A draft with only the place given: coordinates, or a city and country, or both.</summary>
    private static object Placed(string title, string? city, string? country, decimal? latitude = null, decimal? longitude = null, bool published = false)
        => new { title, type = "villa", status = "completed", year = 2022, city, country, latitude, longitude, published };

    private static bool Near(TestPlaces.Town town, decimal? latitude, decimal? longitude)
        => Math.Abs(latitude!.Value - town.Latitude) <= WebsitePlaces.Spread && Math.Abs(longitude!.Value - town.Longitude) <= WebsitePlaces.Spread;

    [Fact]
    public async Task The_country_and_city_come_from_the_lists()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var countries = await (await Client.GetAsync("/api/website/places/countries", superAdmin.AccessToken)).ReadAsync<List<WebsiteCountryResponse>>();
        countries.ShouldContain(new WebsiteCountryResponse("LB", "Lebanon"));
        var cities = await (await Client.GetAsync("/api/website/places/cities?country=LB&search=jb", superAdmin.AccessToken)).ReadAsync<List<WebsiteCityResponse>>();
        cities.Select(c => c.Name).ShouldBe(["Jbeil"]);
        var biggestFirst = await (await Client.GetAsync("/api/website/places/cities?country=Lebanon", superAdmin.AccessToken)).ReadAsync<List<WebsiteCityResponse>>();
        biggestFirst[0].Name.ShouldBe("Beirut");
        await (await Client.GetAsync("/api/website/places/cities?country=Atlantis", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // a place not in the lists saves as a draft, but is never published
        var thumbnail = await PngAsync(superAdmin);
        var hero = await PngAsync(superAdmin);
        object Ready(string title, string? city, string? country, bool published) => new
        {
            title, type = "villa", status = "completed", year = 2022, city, country, description = "A villa.", thumbnailFileId = thumbnail, heroFileId = hero, published,
        };
        var unknown = await CreateAsync(superAdmin, Ready(Unique("Gotham"), "Gotham", "Lebanon", published: false));
        unknown.Latitude.ShouldBeNull();
        (await (await Client.PutJsonAsync($"/api/website/projects/{unknown.Id}", Ready(unknown.Title, "Gotham", "Lebanon", published: true), superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WebsitePlaceNotFound)).Message.ShouldContain("Lebanon's list");
        (await (await Client.PutJsonAsync($"/api/website/projects/{unknown.Id}", Ready(unknown.Title, null, "Atlantis", published: true), superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.WebsitePlaceNotFound)).Message.ShouldContain("country from the list");
        // spelt any way, a name is kept as the list has it
        var spelt = await UpdateAsync(superAdmin, unknown.Id, Ready(unknown.Title, "  BEIRUT ", "lebanon", published: true));
        (spelt.City, spelt.Country, spelt.CountryCode).ShouldBe(("Beirut", "Lebanon", "LB"));
    }

    [Fact]
    public async Task Coordinates_find_their_place_and_a_city_or_country_alone_gets_a_point_kept_until_it_changes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // coordinates win: the country and the nearby city are found from them
        var exact = await CreateAsync(superAdmin, Placed(Unique("Jbeil villa"), null, null, 34.121000m, 35.649000m));
        (exact.City, exact.Country, exact.CountryCode, exact.Latitude, exact.PositionApproximate).ShouldBe(("Jbeil", "Lebanon", "LB", 34.121000m, false));
        // far from any town: the country, and no city
        var remote = await CreateAsync(superAdmin, Placed(Unique("Cedars cabin"), null, null, 34.700000m, 36.300000m));
        (remote.City, remote.Country).ShouldBe((null, "Lebanon"));

        // a city alone: a point near it, a different one for each project
        var first = await CreateAsync(superAdmin, Placed(Unique("Beirut loft"), "Beirut", "Lebanon"));
        var second = await CreateAsync(superAdmin, Placed(Unique("Beirut flat"), "Beirut", "Lebanon"));
        foreach (var p in new[] { first, second })
        {
            p.PositionApproximate.ShouldBeTrue();
            Near(TestPlaces.Beirut, p.Latitude, p.Longitude).ShouldBeTrue();
        }

        (first.Latitude, first.Longitude).ShouldNotBe((second.Latitude, second.Longitude));

        // sent back unchanged with the same city, the point stays, and the map isn't asked
        var lookups = api.Geocoder.Lookups;
        var kept = await UpdateAsync(superAdmin, first.Id, Placed(first.Title, "Beirut", "Lebanon", first.Latitude, first.Longitude));
        (kept.Latitude, kept.Longitude, kept.PositionApproximate).ShouldBe((first.Latitude, first.Longitude, true));
        api.Geocoder.Lookups.ShouldBe(lookups);

        // another city: a point near it
        var moved = await UpdateAsync(superAdmin, first.Id, Placed(first.Title, "Jbeil", "Lebanon", first.Latitude, first.Longitude));
        Near(TestPlaces.Jbeil, moved.Latitude, moved.Longitude).ShouldBeTrue();

        // a country alone: a point near one of its towns, and no city
        var country = await CreateAsync(superAdmin, Placed(Unique("Somewhere"), null, "Lebanon"));
        country.City.ShouldBeNull();
        country.PositionApproximate.ShouldBeTrue();
        TestPlaces.Lebanese.ShouldContain(t => Near(t, country.Latitude, country.Longitude));

        // real coordinates replace the picked point
        var typed = await UpdateAsync(superAdmin, country.Id, Placed(country.Title, null, "Lebanon", 34.255000m, 35.659000m));
        (typed.City, typed.Latitude, typed.PositionApproximate).ShouldBe(("Batroun", 34.255000m, false));

        // published, the website reads the country alone and that the point is picked
        var thumbnail = await PngAsync(superAdmin);
        var hero = await PngAsync(superAdmin);
        var published = await UpdateAsync(superAdmin, second.Id, new
        {
            title = second.Title, type = "villa", status = "completed", year = 2022, city = (string?)null, country = "Lebanon",
            description = "A flat.", thumbnailFileId = thumbnail, heroFileId = hero, published = true,
        });
        var shown = (await PublicProjectsAsync()).Single(p => p.Id == published.Id).Location;
        (shown.City, shown.Country, shown.CountryCode, shown.Approximate).ShouldBe((null, "Lebanon", "LB", true));
    }

    [Fact]
    public async Task With_the_map_service_down_a_picked_point_is_its_town_s_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Geocoder.Down = true;
        try
        {
            var project = await CreateAsync(superAdmin, Placed(Unique("Offline"), "Tripoli", "Lebanon"));
            (project.Latitude, project.Longitude, project.PositionApproximate).ShouldBe((TestPlaces.Tripoli.Latitude, TestPlaces.Tripoli.Longitude, true));
        }
        finally
        {
            api.Geocoder.Down = false;
        }
    }

    [Fact]
    public async Task An_office_takes_its_coordinates_or_its_city()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var address = Unique("Gemmayze");

        var byCity = await (await Client.PostJsonAsync("/api/website/locations", new { city = "Beirut", country = "Lebanon", address, phone = "+961 1 234 567" }, token))
            .ReadAsync<WebsiteContentResponse>();
        var office = byCity.Locations.Single(l => l.Address == address);
        (office.CountryCode, office.PositionApproximate).ShouldBe(("LB", true));
        Near(TestPlaces.Beirut, office.Latitude, office.Longitude).ShouldBeTrue();

        var byPoint = await (await Client.PutJsonAsync($"/api/website/locations/{office.Id}",
            new { latitude = 34.121000m, longitude = 35.649000m, address, phone = "+961 9 540 000" }, token)).ReadAsync<WebsiteContentResponse>();
        var moved = byPoint.Locations.Single(l => l.Id == office.Id);
        (moved.City, moved.Country, moved.PositionApproximate).ShouldBe(("Jbeil", "Lebanon", false));

        await (await Client.PostJsonAsync("/api/website/locations", new { address, phone = "1" }, token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/website/locations", new { city = "Gotham", country = "Lebanon", address, phone = "1" }, token))
            .ShouldFailWithAsync(422, ErrorCodes.WebsitePlaceNotFound);
        // an office is a real address: a country alone isn't enough
        await (await Client.PostJsonAsync("/api/website/locations", new { country = "Lebanon", address, phone = "1" }, token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    private static string WebsiteSlug(string title) => WebsiteRules.SlugOf(title);
}
