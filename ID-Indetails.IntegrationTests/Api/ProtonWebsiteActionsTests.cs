using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The rest of Proton's website actions, with a scripted model: the home page's moving strip, a project's pictures
/// (description, removal, order), the order of the services, offices and social links, the social links, and removing
/// the favicon. Each is strong, with a phrase typed, goes through the Website area's service, and is refused when the
/// thing changed since the card. The website's content is shared by every test: they follow their own items and put
/// back the strip and the orders they change.
/// </summary>
public class ProtonWebsiteActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static string Unique(string title) => $"{title} {Guid.NewGuid().ToString("N")[..6]}";

    /// <param name="said">What the user typed: an address on a card must come from it.</param>
    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    /// <summary>A read tool's data, on a question of its own.</summary>
    private async Task<JsonNode> ReadAsync(AuthResponse who, string tool, object input)
    {
        var (_, result) = await ProposeAsync(who, tool, input, "Tell me about the website.");
        result["status"]!.GetValue<string>().ShouldBe("ok");
        return result["data"]!;
    }

    private Task<HttpResponseMessage> TryConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction.ShouldNotBeNull().Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

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

    private Task<WebsiteContentResponse> ContentAsync(AuthResponse who) => OkAsync<WebsiteContentResponse>(Client.GetAsync("/api/website/content", who.AccessToken));

    private Task<WebsiteProjectsResponse> ProjectsAsync(AuthResponse who) => OkAsync<WebsiteProjectsResponse>(Client.GetAsync("/api/website/projects", who.AccessToken));

    private Task<WebsiteProjectResponse> ProjectAsync(AuthResponse who, long id) => OkAsync<WebsiteProjectResponse>(Client.GetAsync($"/api/website/projects/{id}", who.AccessToken));

    /// <summary>A real photo: the website takes only pictures the server can make copies of.</summary>
    private async Task<long> PhotoAsync(AuthResponse who)
    {
        var response = await FileTests.UploadAsync(Client, FileTests.Photo, "plan.png", who.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<FileResponse>()).Id;
    }

    private Task<WebsiteProjectResponse> DraftAsync(AuthResponse who, string title)
        => OkAsync<WebsiteProjectResponse>(Client.PostJsonAsync("/api/website/projects",
            new { title = Unique(title), type = "villa", status = "in_progress", year = 2026, published = false }, who.AccessToken));

    private async Task<WebsiteProjectResponse> PublishedAsync(AuthResponse who, string title)
        => await OkAsync<WebsiteProjectResponse>(Client.PostJsonAsync("/api/website/projects",
            new
            {
                title = Unique(title), type = "villa", status = "completed", year = 2022, client = "The Rizk family", city = "Beirut", country = "Lebanon",
                latitude = 33.893800m, longitude = 35.501800m, description = "A family villa above the sea.",
                thumbnailFileId = await PhotoAsync(who), heroFileId = await PhotoAsync(who), heroAlt = "The villa at dusk", published = true,
            }, who.AccessToken));

    private async Task<WebsiteImageResponse> AddPictureAsync(AuthResponse who, long projectId, string alt, bool concept = false)
    {
        var project = await OkAsync<WebsiteProjectResponse>(Client.PostJsonAsync($"/api/website/projects/{projectId}/images", new { fileId = await PhotoAsync(who), alt, concept }, who.AccessToken));
        return (concept ? project.ConceptImages : project.Gallery).Last();
    }

    private static List<long> StripOf(WebsiteProjectsResponse list)
        => list.Projects.Where(p => p.ConveyorOrder is not null).OrderBy(p => p.ConveyorOrder).Select(p => p.Id).ToList();

    [Fact]
    public async Task The_moving_strip_is_set_with_its_phrase_typed_and_refused_for_a_draft_or_when_it_changed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var first = await PublishedAsync(superAdmin, "Strip one");
        var second = await PublishedAsync(superAdmin, "Strip two");
        var draft = await DraftAsync(superAdmin, "Strip draft");
        var original = StripOf(await ProjectsAsync(superAdmin));
        try
        {
            var (_, refused) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = new[] { first.Id, draft.Id } });
            refused["status"]!.GetValue<string>().ShouldBe("invalid");
            refused["message"]!.GetValue<string>().ShouldContain("isn't published");
            var (_, unknown) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = new[] { first.Id, long.MaxValue } });
            unknown["message"]!.GetValue<string>().ShouldContain("There is no website project");

            var (card, _) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = new[] { second.Id, first.Id } });
            var preview = card.PendingAction!.Preview;
            (preview.Confirm, preview.Phrase, preview.Area).ShouldBe((ProtonConfirm.Typed, ProposeWebsiteConveyorTool.StripPhrase, DomainCodes.Website));
            preview.Changes.ShouldContain(c => c.Label == second.Title && c.After == "position 1");
            preview.Changes.ShouldContain(c => c.Label == first.Title && c.After == "position 2");
            await (await TryConfirmAsync(superAdmin, card, "strip")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
            await ConfirmAsync(superAdmin, card, "Moving Strip");
            StripOf(await ProjectsAsync(superAdmin)).ShouldBe([second.Id, first.Id]);

            // the same strip again is not a change
            var (_, same) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = new[] { second.Id, first.Id } });
            same["message"]!.GetValue<string>().ShouldContain("already the moving strip");

            // someone else set the strip after the card: refused, not applied over it
            var (stale, _) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = new[] { first.Id } });
            (await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = new[] { first.Id, second.Id } }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            await (await TryConfirmAsync(superAdmin, stale, ProposeWebsiteConveyorTool.StripPhrase)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
            StripOf(await ProjectsAsync(superAdmin)).ShouldBe([first.Id, second.Id]);

            // an empty list empties the strip
            var (clear, _) = await ProposeAsync(superAdmin, "propose_website_conveyor", new { website_project_ids = Array.Empty<long>() });
            clear.PendingAction!.Preview.Changes.ShouldAllBe(c => c.After == "taken out");
            await ConfirmAsync(superAdmin, clear, ProposeWebsiteConveyorTool.StripPhrase);
            StripOf(await ProjectsAsync(superAdmin)).ShouldBeEmpty();
        }
        finally
        {
            await Client.PutJsonAsync("/api/website/conveyor", new { projectIds = original }, superAdmin.AccessToken);
        }
    }

    [Fact]
    public async Task A_pictures_description_is_changed_and_a_picture_removed_with_the_projects_title_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await DraftAsync(superAdmin, "Pictured");
        var pool = await AddPictureAsync(superAdmin, project.Id, "The pool");
        var garden = await AddPictureAsync(superAdmin, project.Id, "The garden");

        // the model finds the picture ids by reading the project
        var read = await ReadAsync(superAdmin, "get_website_projects", new { website_project_id = project.Id });
        var row = read["projects"]!.AsArray().ShouldHaveSingleItem()!;
        row["gallery"]!.AsArray().Select(p => p!["id"]!.GetValue<long>()).ShouldBe([pool.Id, garden.Id]);
        row["gallery"]![0]!["alt"]!.GetValue<string>().ShouldBe("The pool");

        var (change, _) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "update", website_project_id = project.Id, picture_id = pool.Id, alt = "The pool at dusk" });
        var preview = change.PendingAction!.Preview;
        (preview.Confirm, preview.Phrase, preview.Area).ShouldBe((ProtonConfirm.Typed, project.Title, DomainCodes.Website));
        preview.Changes.ShouldBe([new ProtonChange("Description of a picture in the gallery", "The pool", "The pool at dusk")]);
        await (await TryConfirmAsync(superAdmin, change, "Pictured")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, change, project.Title);
        (await ProjectAsync(superAdmin, project.Id)).Gallery.Single(g => g.Id == pool.Id).Alt.ShouldBe("The pool at dusk");

        var (_, nothing) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "update", website_project_id = project.Id, picture_id = pool.Id, alt = "The pool at dusk" });
        nothing["message"]!.GetValue<string>().ShouldContain("changes nothing");
        var (_, elsewhere) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "remove", website_project_id = project.Id, picture_id = long.MaxValue });
        elsewhere["status"]!.GetValue<string>().ShouldBe("not_found");

        // someone else described it after the card: refused, not applied over it
        var (stale, _) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "update", website_project_id = project.Id, picture_id = garden.Id, alt = "A terrace" });
        (await Client.PutJsonAsync($"/api/website/projects/{project.Id}/images/{garden.Id}", new { alt = "The garden in spring" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await TryConfirmAsync(superAdmin, stale, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        var (remove, _) = await ProposeAsync(superAdmin, "propose_website_picture", new { step = "remove", website_project_id = project.Id, picture_id = pool.Id });
        remove.PendingAction!.Preview.Irreversible.ShouldBeTrue();
        await (await TryConfirmAsync(superAdmin, remove)).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, remove, project.Title);
        (await ProjectAsync(superAdmin, project.Id)).Gallery.Select(g => g.Id).ShouldBe([garden.Id]);
    }

    [Fact]
    public async Task A_projects_gallery_and_concept_pictures_are_ordered_apart_and_a_partial_order_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await DraftAsync(superAdmin, "Ordered");
        var one = await AddPictureAsync(superAdmin, project.Id, "One");
        var two = await AddPictureAsync(superAdmin, project.Id, "Two");
        var three = await AddPictureAsync(superAdmin, project.Id, "Three");
        var sketch = await AddPictureAsync(superAdmin, project.Id, "Sketch", concept: true);
        var model = await AddPictureAsync(superAdmin, project.Id, "Model", concept: true);

        var (order, _) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "order", website_project_id = project.Id, picture_ids = new[] { three.Id, one.Id, two.Id } });
        order.PendingAction!.Preview.Title.ShouldContain("the gallery");
        order.PendingAction.Preview.Changes.ShouldBe(
            [new ProtonChange("Three", "position 3", "position 1"), new ProtonChange("One", "position 1", "position 2"), new ProtonChange("Two", "position 2", "position 3")],
            ignoreOrder: true);
        await ConfirmAsync(superAdmin, order, project.Title);
        var ordered = await ProjectAsync(superAdmin, project.Id);
        ordered.Gallery.Select(g => g.Id).ShouldBe([three.Id, one.Id, two.Id]);
        ordered.ConceptImages.Select(g => g.Id).ShouldBe([sketch.Id, model.Id]);

        var (concept, _) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "order", website_project_id = project.Id, picture_ids = new[] { model.Id, sketch.Id } });
        concept.PendingAction!.Preview.Title.ShouldContain("the concept pictures");
        await ConfirmAsync(superAdmin, concept, project.Title);
        var reordered = await ProjectAsync(superAdmin, project.Id);
        reordered.ConceptImages.Select(g => g.Id).ToList().ShouldBe([model.Id, sketch.Id]);
        reordered.Gallery.Select(g => g.Id).ToList().ShouldBe([three.Id, one.Id, two.Id]);

        // not every picture of one list, or pictures of both lists, is not an order
        foreach (var ids in new[] { new[] { one.Id, two.Id }, [three.Id, sketch.Id], [three.Id, three.Id, one.Id], Array.Empty<long>() })
        {
            var (_, refused) = await ProposeAsync(superAdmin, "propose_website_picture", new { step = "order", website_project_id = project.Id, picture_ids = ids });
            refused["status"]!.GetValue<string>().ShouldBe("invalid");
        }

        var (_, already) = await ProposeAsync(superAdmin, "propose_website_picture",
            new { step = "order", website_project_id = project.Id, picture_ids = new[] { three.Id, one.Id, two.Id } });
        already["message"]!.GetValue<string>().ShouldContain("already the order");
    }

    [Fact]
    public async Task Services_offices_and_links_are_ordered_by_name_and_put_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var title = Unique("Lighting");
        var other = Unique("Acoustics");
        var label = Unique("Pinterest");
        var otherLabel = Unique("Behance");
        var addressA = Unique("Mar Mikhael");
        var addressB = Unique("Gemmayze");
        foreach (var service in new[] { title, other })
        {
            (await Client.PostJsonAsync("/api/website/services", new { title = service, description = "Plain text." }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        foreach (var address in new[] { addressA, addressB })
        {
            (await Client.PostJsonAsync("/api/website/locations", new { city = "Beirut", country = "Lebanon", address, phone = "+961 1 234567" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        foreach (var link in new[] { label, otherLabel })
        {
            (await Client.PostJsonAsync("/api/website/social-links", new { label = link, url = "https://example.com/" + link.Replace(' ', '-') }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        var before = await ContentAsync(superAdmin);
        var services = before.Services.Single(s => s.Title == title);
        var acoustics = before.Services.Single(s => s.Title == other);
        var officeA = before.Locations.Single(l => l.Address == addressA);
        var officeB = before.Locations.Single(l => l.Address == addressB);
        var pinterest = before.SocialLinks.Single(l => l.Label == label);
        var behance = before.SocialLinks.Single(l => l.Label == otherLabel);
        try
        {
            // services: by title, all of them, mine first
            var allServices = before.Services.Select(s => s.Title).Where(t => t != title && t != other).Prepend(title).Prepend(other).ToList();
            var (servicesCard, _) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "services", names = allServices });
            var card = servicesCard.PendingAction!.Preview;
            (card.Confirm, card.Phrase, card.Area).ShouldBe((ProtonConfirm.Typed, "services order", DomainCodes.Website));
            card.Changes.ShouldContain(c => c.Label == other && c.After == "position 1");
            card.Changes.ShouldContain(c => c.Label == title && c.After == "position 2");
            await (await TryConfirmAsync(superAdmin, servicesCard, "services")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
            await ConfirmAsync(superAdmin, servicesCard, "services order");
            (await ContentAsync(superAdmin)).Services.Take(2).Select(s => s.Id).ShouldBe([acoustics.Id, services.Id]);

            // not every service named, or one that doesn't exist
            var (_, missing) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "services", names = new[] { title } });
            missing["message"]!.GetValue<string>().ShouldContain("Missing");
            var (_, unknown) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "services", names = allServices.Append("Nothing like this " + Guid.NewGuid()) });
            unknown["message"]!.GetValue<string>().ShouldContain("There is no service called");

            // offices: other offices may share a city, so mine are named by city and address, the rest by city
            var allOffices = before.Locations.Where(l => l.Id != officeA.Id && l.Id != officeB.Id).Select(l => l.City)
                .Prepend($"Beirut, {addressA}").Prepend($"Beirut, {addressB}").ToList();
            var (offices, _) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "offices", names = allOffices });
            offices.PendingAction!.Preview.Phrase.ShouldBe("offices order");
            await ConfirmAsync(superAdmin, offices, "offices order");
            (await ContentAsync(superAdmin)).Locations.Take(2).Select(l => l.Id).ShouldBe([officeB.Id, officeA.Id]);

            // links
            var allLinks = before.SocialLinks.Select(l => l.Label).Where(l => l != label && l != otherLabel).Prepend(label).Prepend(otherLabel).ToList();
            var (links, _) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "links", names = allLinks });
            links.PendingAction!.Preview.Phrase.ShouldBe("links order");
            await ConfirmAsync(superAdmin, links, "links order");
            (await ContentAsync(superAdmin)).SocialLinks.Take(2).Select(l => l.Id).ShouldBe([behance.Id, pinterest.Id]);

            // someone else moved a service after the card: refused, not applied over it
            var current = (await ContentAsync(superAdmin)).Services;
            var names = current.Select(s => s.Title).ToList();
            var (stale, _) = await ProposeAsync(superAdmin, "propose_website_order", new { list = "services", names = names.Skip(1).Append(names[0]) });
            (await Client.PutJsonAsync("/api/website/services/order", new { ids = current.Select(s => s.Id).Reverse() }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            await (await TryConfirmAsync(superAdmin, stale, "services order")).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        }
        finally
        {
            await Client.PutJsonAsync("/api/website/services/order", new { ids = before.Services.Select(s => s.Id) }, superAdmin.AccessToken);
            await Client.PutJsonAsync("/api/website/locations/order", new { ids = before.Locations.Select(l => l.Id) }, superAdmin.AccessToken);
            await Client.PutJsonAsync("/api/website/social-links/order", new { ids = before.SocialLinks.Select(l => l.Id) }, superAdmin.AccessToken);

            var restored = await ContentAsync(superAdmin);
            restored.Services.Select(s => s.Id).ToList().ShouldBe(before.Services.Select(s => s.Id).ToList());
            restored.Locations.Select(l => l.Id).ToList().ShouldBe(before.Locations.Select(l => l.Id).ToList());
            restored.SocialLinks.Select(l => l.Id).ToList().ShouldBe(before.SocialLinks.Select(l => l.Id).ToList());
            foreach (var id in new[] { services.Id, acoustics.Id })
            {
                await Client.DeleteAsync($"/api/website/services/{id}", superAdmin.AccessToken);
            }

            foreach (var id in new[] { officeA.Id, officeB.Id })
            {
                await Client.DeleteAsync($"/api/website/locations/{id}", superAdmin.AccessToken);
            }

            foreach (var id in new[] { pinterest.Id, behance.Id })
            {
                await Client.DeleteAsync($"/api/website/social-links/{id}", superAdmin.AccessToken);
            }
        }
    }

    [Fact]
    public async Task A_social_link_is_added_changed_and_removed_with_its_name_typed_and_only_an_address_the_user_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var label = Unique("Instagram");
        var address = $"https://instagram.com/{Guid.NewGuid().ToString("N")[..8]}";

        // an address the user didn't type, or not an https one, is refused before a card exists
        var (_, invented) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "add", label, url = address }, "Add our Instagram to the website footer.");
        invented["status"]!.GetValue<string>().ShouldBe("invalid");
        invented["message"]!.GetValue<string>().ShouldContain("typed");
        var (_, insecure) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "add", label, url = "http://instagram.com/x" }, "Add http://instagram.com/x as Instagram.");
        insecure["status"]!.GetValue<string>().ShouldBe("invalid");
        var (_, noLabel) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "add", url = address }, $"Add {address}");
        noLabel["message"]!.GetValue<string>().ShouldContain("name and its full");

        var (add, _) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "add", label, url = address }, $"Add {label} to the website footer: {address}");
        var preview = add.PendingAction!.Preview;
        (preview.Confirm, preview.Phrase, preview.Area).ShouldBe((ProtonConfirm.Typed, label, DomainCodes.Website));
        preview.Changes.ShouldBe([new ProtonChange("Name", null, label), new ProtonChange("Address", null, address)], ignoreOrder: true);
        await (await TryConfirmAsync(superAdmin, add, "Instagram")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, add, label);
        var link = (await ContentAsync(superAdmin)).SocialLinks.Single(l => l.Label == label);
        link.Url.ShouldBe(address);

        // the model finds the link and its id by reading the website
        var read = await ReadAsync(superAdmin, "get_website_content", new { });
        var shown = read["social_links"]!.AsArray().Single(l => l!["id"]!.GetValue<long>() == link.Id)!;
        (shown["label"]!.GetValue<string>(), shown["url"]!.GetValue<string>()).ShouldBe((label, address));

        // renaming it keeps the address the data holds; a new address must be typed
        var renamed = Unique("Insta");
        var (rename, _) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "update", social_link_id = link.Id, label = renamed, url = address }, $"Call the {label} link {renamed}");
        rename.PendingAction!.Preview.Changes.ShouldBe([new ProtonChange("Name", label, renamed)]);
        rename.PendingAction.Preview.Phrase.ShouldBe(label);
        await ConfirmAsync(superAdmin, rename, label);
        (await ContentAsync(superAdmin)).SocialLinks.Single(l => l.Id == link.Id).Label.ShouldBe(renamed);

        var other = "https://www.instagram.com/idindetails";
        var (_, untyped) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "update", social_link_id = link.Id, url = other }, "Change the address of that link.");
        untyped["status"]!.GetValue<string>().ShouldBe("invalid");
        var (move, _) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "update", social_link_id = link.Id, url = other }, $"Its address is now {other}");
        move.PendingAction!.Preview.Changes.ShouldBe([new ProtonChange("Address", address, other)]);

        // someone else changed it after the card: refused, not applied over it
        (await Client.PutJsonAsync($"/api/website/social-links/{link.Id}", new { label = renamed, url = "https://example.com/else" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await TryConfirmAsync(superAdmin, move, renamed)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        var (remove, _) = await ProposeAsync(superAdmin, "propose_website_social_link", new { step = "delete", social_link_id = link.Id });
        (remove.PendingAction!.Preview.Phrase, remove.PendingAction.Preview.Irreversible).ShouldBe((renamed, true));
        await ConfirmAsync(superAdmin, remove, renamed);
        (await ContentAsync(superAdmin)).SocialLinks.ShouldNotContain(l => l.Id == link.Id);
    }

    [Fact]
    public async Task The_favicon_is_removed_with_a_phrase_typed_and_only_when_there_is_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var original = (await ContentAsync(superAdmin)).FaviconFileId;
        try
        {
            if (original is not null)
            {
                (await Client.DeleteAsync("/api/website/favicon", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            }

            var (_, none) = await ProposeAsync(superAdmin, "propose_remove_website_favicon", new { });
            none["message"]!.GetValue<string>().ShouldContain("no favicon");

            var favicon = await PhotoAsync(superAdmin);
            (await Client.PutJsonAsync("/api/website/favicon", new { fileId = favicon }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            var (card, _) = await ProposeAsync(superAdmin, "propose_remove_website_favicon", new { });
            var preview = card.PendingAction!.Preview;
            (preview.Confirm, preview.Phrase, preview.Irreversible, preview.Area).ShouldBe((ProtonConfirm.Typed, ProposeRemoveWebsiteFaviconTool.RemovePhrase, true, DomainCodes.Website));
            await (await TryConfirmAsync(superAdmin, card, "favicon")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);

            // replaced after the card: the card is for a favicon that is gone
            var replacement = await PhotoAsync(superAdmin);
            (await Client.PutJsonAsync("/api/website/favicon", new { fileId = replacement }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            await (await TryConfirmAsync(superAdmin, card, "Remove favicon")).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
            (await ContentAsync(superAdmin)).FaviconFileId.ShouldBe(replacement);

            var (again, _) = await ProposeAsync(superAdmin, "propose_remove_website_favicon", new { });
            await ConfirmAsync(superAdmin, again, "Remove favicon");
            (await ContentAsync(superAdmin)).FaviconFileId.ShouldBeNull();
        }
        finally
        {
            if (original is { } kept)
            {
                await Client.PutJsonAsync("/api/website/favicon", new { fileId = kept }, superAdmin.AccessToken);
            }
        }
    }

    [Fact]
    public async Task Without_website_manage_none_of_them_is_proposed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var grant = await Client.PutJsonAsync(
            $"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WebsiteView, action = "grant", reason = "Integration test" } } },
            superAdmin.AccessToken);
        grant.IsSuccessStatusCode.ShouldBeTrue(await grant.Content.ReadAsStringAsync());
        var project = await DraftAsync(superAdmin, "Not theirs");

        // looking at the website is not changing it
        (await ReadAsync(admin, "get_website_content", new { })).ShouldNotBeNull();
        (string Tool, object Input)[] attempts =
        [
            ("propose_website_conveyor", new { website_project_ids = new[] { project.Id } }),
            ("propose_website_picture", new { step = "remove", website_project_id = project.Id, picture_id = 1 }),
            ("propose_website_order", new { list = "services", names = new[] { "A" } }),
            ("propose_website_social_link", new { step = "add", label = "X", url = "https://example.com" }),
            ("propose_remove_website_favicon", new { }),
        ];
        foreach (var (tool, input) in attempts)
        {
            var (reply, result) = await ProposeAsync(admin, tool, input, "Please do it: https://example.com");
            result["status"]!.GetValue<string>().ShouldBe("forbidden", tool);
            reply.PendingActions.ShouldBeEmpty(tool);
        }
    }
}
