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

    private async Task<long> PngAsync(AuthResponse who) => (await FileTests.UploadPngAsync(Client, who.AccessToken)).Id;

    private static string Unique(string title) => $"{title} {Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>A project with everything the website shows.</summary>
    private static object Full(
        string title, long? thumbnail, long? hero, bool published = true, long? projectId = null, string? status = "completed",
        int? year = 2022, string? slug = null)
        => new
        {
            title,
            slug,
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
            heroCredit = new { photographer = "A. Photographer", source = "Unsplash", url = "https://unsplash.com/photos/abc" },
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
        draft.MissingToPublish.ShouldContain("a thumbnail");
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
        shown.Location.ShouldBe(new PublicPlace("Beirut", "Lebanon", 33.893800m, 35.501800m));
        shown.Thumbnail.ShouldBe($"{Public}/files/{thumbnail}");
        shown.Hero.Src.ShouldBe($"{Public}/files/{hero}");
        shown.Hero.Alt.ShouldBe("The villa at dusk");
        shown.Hero.Credit!.Source.ShouldBe("Unsplash");
        shown.ShowInConveyor.ShouldBeFalse();
        (await (await Client.GetAsync($"{Public}/projects/{draft.Slug}")).ReadAsync<PublicWebsiteProject>()).Id.ShouldBe(draft.Id);

        // the address is the project's own
        await (await Client.PostJsonAsync("/api/website/projects", Full(Unique("Other"), null, null, published: false, slug: draft.Slug), superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.WebsiteSlugTaken);
        await (await Client.PostJsonAsync("/api/website/projects", Full(Unique("Other"), null, null, published: false, slug: "Not A Slug"), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

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
            new { fileId = await PngAsync(superAdmin), alt = "The terrace", credit = new { photographer = "B. Lens", source = "Pexels", url = "https://pexels.com/p/1" } },
            superAdmin.AccessToken)).ReadAsync<WebsiteProjectResponse>();
        var (pool, terrace) = (withTwo.Gallery[0], withTwo.Gallery[1]);
        terrace.Credit!.Photographer.ShouldBe("B. Lens");

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
    public async Task The_public_site_serves_only_its_own_pictures()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var published = await PublishedAsync(superAdmin, "Pictured");
        var draft = await CreateAsync(superAdmin, Full(Unique("Hidden"), await PngAsync(superAdmin), null, published: false));
        var loose = await PngAsync(superAdmin);

        var picture = await Client.GetAsync($"{Public}/files/{published.ThumbnailFileId}");
        ((int)picture.StatusCode).ShouldBe(200);
        (await picture.Content.ReadAsByteArrayAsync()).ShouldBe(FileTests.Png);
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

        var city = Unique("Beirut");
        var located = await (await Client.PostJsonAsync(
            "/api/website/locations",
            new { city, country = "Lebanon", latitude = 33.8938m, longitude = 35.5018m, address = "Gemmayze, Beirut", phone = "+961 1 234 567" },
            token)).ReadAsync<WebsiteContentResponse>();
        var office = located.Locations.Single(l => l.City == city);
        (await (await Client.GetAsync($"{Public}/locations")).ReadAsync<List<PublicWebsiteLocation>>()).ShouldContain(l => l.Id == office.Id && l.Lat == 33.8938m);
        await (await Client.PostJsonAsync("/api/website/locations", new { city, country = "Lebanon", latitude = 95m, longitude = 0m, address = "x", phone = "1" }, token))
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

    private static string WebsiteSlug(string title) => ID_Indetails.api.Business.Website.WebsiteRules.SlugOf(title);
}
