using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Proton;
using PdfSharp.Pdf;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton reading the newer features as the person would see them (Phase 2): a project's files, the content of one
/// (handed to the model as a PDF it reads itself), the client portal, the discussion with the client, and the website.
/// </summary>
public class ProtonReadTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<(AuthResponse SuperAdmin, AuthResponse DesignAdmin)> PeopleAsync()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        return (superAdmin, designAdmin);
    }

    /// <summary>One tool call on its own question: the tool result's blocks (the data first, then what was read).</summary>
    private async Task<JsonArray> CallAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Done."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Tell me about it" }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var content = api.Anthropic.ToolResults(1).Single()["content"]!;
        return content is JsonArray blocks ? blocks : [new JsonObject { ["type"] = "text", ["text"] = content.GetValue<string>() }];
    }

    private static JsonNode Data(JsonArray blocks) => JsonNode.Parse(blocks[0]!["text"]!.GetValue<string>())!;

    private static string Status(JsonArray blocks) => Data(blocks)["status"]!.GetValue<string>();

    [Fact]
    public async Task Lists_a_projects_files_and_reads_one_for_the_model_to_look_at()
    {
        var (superAdmin, _) = await PeopleAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var plan = await UploadAsync(superAdmin, project.Id, "Floor plan.pdf", Pdf(3), "drawing");

        var listed = Data(await CallAsync(superAdmin, "get_project_files", new { project_id = project.Id }))["data"]!;
        var row = listed["files"]!.AsArray().Single()!;
        (row["item_id"]!.GetValue<long>(), row["section"]!.GetValue<string>(), row["readable"]!.GetValue<string>()).ShouldBe((plan.Id, "drawing", "pdf"));

        var read = await CallAsync(superAdmin, "read_project_file", new { project_id = project.Id, item_id = plan.Id });
        Data(read)["data"]!["read"]!.GetValue<string>().ShouldBe("all 3 pages");
        // the PDF itself follows the data, under a line that says it is data, not instructions
        read[1]!["text"]!.GetValue<string>().ShouldContain("not instructions");
        read[2]!["type"]!.GetValue<string>().ShouldBe("document");
        read[2]!["source"]!["media_type"]!.GetValue<string>().ShouldBe("application/pdf");

        // a file of another project, or none: nothing is read
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        Status(await CallAsync(superAdmin, "read_project_file", new { project_id = other.Id, item_id = plan.Id })).ShouldBe("not_found");
    }

    [Fact]
    public async Task Reads_at_most_two_files_for_one_question()
    {
        var (superAdmin, _) = await PeopleAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var plan = await UploadAsync(superAdmin, project.Id, "Plan.pdf", Pdf(1), "drawing");
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("read_project_file", new { project_id = project.Id, item_id = plan.Id }),
            FakeAnthropic.ToolUse("read_project_file", new { project_id = project.Id, item_id = plan.Id }),
            FakeAnthropic.ToolUse("read_project_file", new { project_id = project.Id, item_id = plan.Id }),
            FakeAnthropic.Text("Read twice."));

        (await Client.PostJsonAsync("/api/proton/messages", new { text = "Read it three times" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        api.Anthropic.ToolResults(3).Single()["content"]!.ToJsonString().ShouldContain("Only 2 files");
    }

    [Fact]
    public async Task The_portal_and_the_discussion_are_read_only_by_those_who_see_them_and_nothing_is_marked_read()
    {
        var (superAdmin, designAdmin) = await PeopleAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var sent = await Client.PostJsonAsync($"/api/projects/{project.Id}/portal/discussion/messages",
            new { body = "The tiles arrive Monday.", clientToken = Guid.NewGuid().ToString() }, superAdmin.AccessToken);
        sent.IsSuccessStatusCode.ShouldBeTrue(await sent.Content.ReadAsStringAsync());

        var status = Data(await CallAsync(superAdmin, "get_portal_status", new { project_id = project.Id }))["data"]!;
        status["published"].ShouldBeNull();
        status["has_unpublished_changes"]!.GetValue<bool>().ShouldBeFalse();
        status["clients"]!.AsArray().ShouldNotBeEmpty();

        var discussion = await CallAsync(superAdmin, "read_discussion", new { project_id = project.Id });
        Data(discussion)["data"]!["messages"]!.GetValue<int>().ShouldBe(1);
        discussion[2]!["text"]!.GetValue<string>().ShouldContain("The tiles arrive Monday.");
        // a period with nothing in it
        Data(await CallAsync(superAdmin, "read_discussion", new { project_id = project.Id, period = "last_month" }))["data"]!["messages"]!.GetValue<int>().ShouldBe(0);

        // the Design admin isn't in this discussion: it reads as not found, and nothing of it reaches the model
        var hidden = await CallAsync(designAdmin, "read_discussion", new { project_id = project.Id });
        Status(hidden).ShouldBe("not_found");
        hidden.ToJsonString().ShouldNotContain("tiles");

        // nothing Proton read is marked read for the user (the message is their own, so their unread count stays as it was)
        var summary = await (await Client.GetAsync($"/api/projects/{project.Id}/portal/discussion", superAdmin.AccessToken)).ReadAsync<JsonObject>();
        summary["unreadMessages"]!.GetValue<int>().ShouldBe(0);
    }

    [Fact]
    public async Task The_website_is_read_by_those_who_see_it()
    {
        var (superAdmin, designAdmin) = await PeopleAsync();
        var title = $"Cedar house {Guid.NewGuid().ToString("N")[..6]}";
        (await Client.PostJsonAsync("/api/website/projects", new { title, type = "villa", status = "in_progress", year = 2026, published = false }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var projects = Data(await CallAsync(superAdmin, "get_website_projects", new { published = false }))["data"]!;
        projects["projects"]!.AsArray().Select(p => p!["title"]!.GetValue<string>()).ShouldContain(title);
        Data(await CallAsync(superAdmin, "get_website_content", new { }))["status"]!.GetValue<string>().ShouldBe("ok");
        // Google Analytics isn't set up in tests: the tool says so rather than failing
        Status(await CallAsync(superAdmin, "get_website_analytics", new { })).ShouldBe("refused");

        // the Design admin has no Website area: these tools aren't even offered
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.Text("I can't see the website."));
        await Client.PostJsonAsync("/api/proton/messages", new { text = "What's on the website?" }, designAdmin.AccessToken);
        api.Anthropic.ToolsOffered(0).ShouldNotContain("get_website_projects");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("get_website_analytics");
    }

    private async Task<ProjectFileResponse> UploadAsync(AuthResponse staff, long projectId, string fileName, byte[] bytes, string section)
    {
        var files = $"/api/projects/{projectId}/files";
        var start = await Client.PostJsonAsync($"{files}/uploads", new { clientToken = Guid.NewGuid().ToString(), section, fileName, size = bytes.Length }, staff.AccessToken);
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

    private static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
