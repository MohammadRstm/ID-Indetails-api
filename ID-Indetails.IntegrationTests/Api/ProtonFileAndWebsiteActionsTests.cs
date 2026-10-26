using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Proton.Actions;
using Microsoft.AspNetCore.Hosting;
using PdfSharp.Pdf;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Deletes, access and the website switched on, as an admin would one by one.</summary>
public sealed class ProtonWebsiteApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Proton:Actions:Deletes", "true");
        builder.UseSetting("Proton:Actions:Access", "true");
        builder.UseSetting("Proton:Actions:Website", "true");
        builder.UseSetting("Proton:Actions:MaxConfirmedActionsPerHour", "200");
    }
}

/// <summary>
/// Proton's actions on a project's files, its client portal and the public website (Phase 4), with a scripted model:
/// renaming, restoring and commenting on a file are one Confirm; deleting a file, publishing or discarding the portal,
/// and every change to the website are strong, with a phrase typed; each goes through the existing service.
/// </summary>
public class ProtonFileAndWebsiteActionsTests(ProtonWebsiteApiFactory api) : IClassFixture<ProtonWebsiteApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Please make that change." }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private Task<HttpResponseMessage> TryConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction.ShouldNotBeNull().Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> GetAsync<T>(AuthResponse who, string path) => await (await Client.GetAsync(path, who.AccessToken)).ReadAsync<T>();

    [Fact]
    public async Task A_file_is_renamed_commented_on_and_deleted_with_the_projects_name_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var file = await UploadAsync(superAdmin, project.Id, "Plan.pdf");

        var (rename, _) = await ProposeAsync(superAdmin, "propose_rename_file", new { project_id = project.Id, item_id = file.Id, title = "Ground floor plan" });
        rename.PendingAction!.Preview.Changes.ShouldBe([new ProtonChange("Title", file.Title, "Ground floor plan")], ignoreOrder: true);
        await ConfirmAsync(superAdmin, rename);

        var (comment, _) = await ProposeAsync(superAdmin, "propose_comment_on_file", new { project_id = project.Id, item_id = file.Id, body = "The stairs need checking." });
        comment.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Standard);
        await ConfirmAsync(superAdmin, comment);
        (await GetAsync<List<ProjectFileCommentResponse>>(superAdmin, $"/api/projects/{project.Id}/files/items/{file.Id}/comments"))
            .Single().Body.ShouldBe("The stairs need checking.");

        // a file that isn't deleted can't be brought back
        (await ProposeAsync(superAdmin, "propose_restore_file", new { project_id = project.Id, item_id = file.Id })).Result["message"]!.GetValue<string>().ShouldContain("isn't deleted");

        var (delete, _) = await ProposeAsync(superAdmin, "propose_delete", new { kind = "project_file", id = file.Id, project_id = project.Id });
        (delete.PendingAction!.Preview.Confirm, delete.PendingAction.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, project.Title));
        await (await TryConfirmAsync(superAdmin, delete, "Plan")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(superAdmin, delete, project.Title);
        (await GetAsync<ProjectFilesResponse>(superAdmin, $"/api/projects/{project.Id}/files")).Items.Where(i => !i.Removed).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rename_made_stale_by_someone_else_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var file = await UploadAsync(superAdmin, project.Id, "Plan.pdf");
        var (rename, _) = await ProposeAsync(superAdmin, "propose_rename_file", new { project_id = project.Id, item_id = file.Id, title = "Mine" });

        (await Client.PutJsonAsync($"/api/projects/{project.Id}/files/items/{file.Id}", new { title = "Theirs" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await TryConfirmAsync(superAdmin, rename)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
    }

    [Fact]
    public async Task The_client_portal_is_published_with_the_projects_name_typed_and_only_when_there_is_a_change()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var nothing = (await ProposeAsync(superAdmin, "propose_portal_step", new { project_id = project.Id, step = "publish" })).Result;
        nothing["status"]!.GetValue<string>().ShouldBe("invalid");

        var file = await UploadAsync(superAdmin, project.Id, "Concept.pdf");
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/files/items/{file.Id}/for-client", new { forClient = true }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var (publish, _) = await ProposeAsync(superAdmin, "propose_portal_step", new { project_id = project.Id, step = "publish" });
        publish.PendingAction!.Preview.Phrase.ShouldBe(project.Title);
        publish.PendingAction.Preview.Changes.ShouldContain(c => c.Label == "Files added" && c.After == "1");
        await ConfirmAsync(superAdmin, publish, project.Title);

        var portal = await GetAsync<PortalContentResponse>(superAdmin, $"/api/projects/{project.Id}/portal/content");
        portal.Publication.PublishedAt.ShouldNotBeNull();
        portal.HasUnpublishedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task A_website_project_is_added_changed_and_deleted_with_its_title_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var title = $"Harbour loft {Guid.NewGuid().ToString("N")[..6]}";

        var (add, _) = await ProposeAsync(superAdmin, "propose_website_project",
            new { step = "add", title, type = "residential", status = "in_progress", year = 2026, city = "Beirut", country = "Lebanon" });
        (add.PendingAction!.Preview.Phrase, add.PendingAction.Preview.Area).ShouldBe((title, DomainCodes.Website));
        var added = await ConfirmAsync(superAdmin, add, title);
        var id = added.Refs.ShouldHaveSingleItem().Id;

        var (change, _) = await ProposeAsync(superAdmin, "propose_website_project", new { step = "update", website_project_id = id, client = "The Aouns" });
        change.PendingAction!.Preview.Changes.ShouldBe([new ProtonChange("Client", "none", "The Aouns")], ignoreOrder: true);
        await ConfirmAsync(superAdmin, change, title);
        var shown = await GetAsync<WebsiteProjectResponse>(superAdmin, $"/api/website/projects/{id}");
        (shown.Client, shown.City, shown.Year).ShouldBe(("The Aouns", "Beirut", 2026));

        // changed by someone else after the card: refused, not applied over it
        var (stale, _) = await ProposeAsync(superAdmin, "propose_website_project", new { step = "update", website_project_id = id, year = 2025 });
        (await Client.PutJsonAsync($"/api/website/projects/{id}", new { title, type = "residential", status = "completed", year = 2026, published = false }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await (await TryConfirmAsync(superAdmin, stale, title)).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        var (delete, _) = await ProposeAsync(superAdmin, "propose_website_project", new { step = "delete", website_project_id = id });
        delete.PendingAction!.Preview.Irreversible.ShouldBeTrue();
        await ConfirmAsync(superAdmin, delete, title);
        (await Client.GetAsync($"/api/website/projects/{id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_websites_services_offices_and_construction_page_change_with_a_phrase_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = $"Landscape {Guid.NewGuid().ToString("N")[..6]}";

        await ConfirmAsync(superAdmin, (await ProposeAsync(superAdmin, "propose_website_service", new { step = "add", title = name, description = "Gardens and terraces." })).Reply, name);
        var service = (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).Services.Single(s => s.Title == name);
        await ConfirmAsync(superAdmin, (await ProposeAsync(superAdmin, "propose_website_service", new { step = "update", service_id = service.Id, description = "Gardens." })).Reply, name);
        (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).Services.Single(s => s.Id == service.Id).Description.ShouldBe("Gardens.");
        await ConfirmAsync(superAdmin, (await ProposeAsync(superAdmin, "propose_website_service", new { step = "delete", service_id = service.Id })).Reply, name);
        (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).Services.ShouldNotContain(s => s.Id == service.Id);

        var phone = $"+961 1 {Random.Shared.Next(100_000, 999_999)}";
        await ConfirmAsync(superAdmin, (await ProposeAsync(superAdmin, "propose_website_location",
            new { step = "add", city = "Beirut", country = "Lebanon", address = "Mar Mikhael", phone })).Reply, "Beirut");
        (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).Locations.ShouldContain(l => l.Phone == phone);

        var (down, _) = await ProposeAsync(superAdmin, "propose_website_under_construction", new { under_construction = true });
        down.PendingAction!.Preview.Phrase.ShouldBe(ProposeUnderConstructionTool.DownPhrase);
        await ConfirmAsync(superAdmin, down, ProposeUnderConstructionTool.DownPhrase);
        (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).UnderConstructionSince.ShouldNotBeNull();
        await ConfirmAsync(superAdmin, (await ProposeAsync(superAdmin, "propose_website_under_construction", new { under_construction = false })).Reply, ProposeUnderConstructionTool.UpPhrase);
        (await GetAsync<WebsiteContentResponse>(superAdmin, "/api/website/content")).UnderConstructionSince.ShouldBeNull();
    }

    private async Task<ProjectFileResponse> UploadAsync(AuthResponse staff, long projectId, string fileName)
    {
        using var document = new PdfDocument();
        document.AddPage();
        using var pdf = new MemoryStream();
        document.Save(pdf, false);
        var bytes = pdf.ToArray();

        var files = $"/api/projects/{projectId}/files";
        var start = await Client.PostJsonAsync($"{files}/uploads", new { clientToken = Guid.NewGuid().ToString(), section = "drawing", fileName, size = bytes.Length }, staff.AccessToken);
        ((int)start.StatusCode).ShouldBe(201, await start.Content.ReadAsStringAsync());
        var started = await start.ReadAsync<PortalUploadStartedResponse>();
        var links = await (await Client.PostJsonAsync($"{files}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, staff.AccessToken))
            .ReadAsync<PortalPartLinksResponse>();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await Client.PutAsync(links.Parts[0].Url, part)).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await Client.PostEmptyAsync($"{files}/uploads/{started.UploadId}/complete", staff.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        return (await completed.ReadAsync<ProjectFileUploadCompletedResponse>()).Item!;
    }
}
