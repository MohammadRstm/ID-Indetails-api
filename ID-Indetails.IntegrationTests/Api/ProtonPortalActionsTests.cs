using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton's actions on a Design project's client portal (access, the draft's main pictures and introduction, the discussion
/// with the client) and on the arrangement of its files, with a scripted model: each is proposed as a card, goes through its
/// existing service only on Confirm, and has a refusal.
/// </summary>
public class ProtonPortalActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE1, .. new byte[300]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    private sealed record Setup(AuthResponse SuperAdmin, ProjectResponse Project, string ContactEmail);

    /// <summary>A super admin and a Design client project for a client with one email contact (not yet invited).</summary>
    private async Task<Setup> ClientProjectAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var contact = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = contact, label = "Office" } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        var title = "Project " + Guid.NewGuid().ToString("N")[..8];
        var created = await Client.PostJsonAsync("/api/projects", new { title, domainCode = DomainCodes.Design, clientId = client.Id }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return new Setup(superAdmin, await created.ReadAsync<ProjectResponse>(), contact);
    }

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
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction.ShouldNotBeNull().Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> GetAsync<T>(AuthResponse who, string path)
    {
        var response = await Client.GetAsync(path, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private static string Portal(long projectId) => $"/api/projects/{projectId}/portal";

    private async Task<PortalClientResponse> ContactAsync(Setup s)
        => (await GetAsync<PortalClientsResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/clients")).Clients.Single(c => c.Email.Equals(s.ContactEmail, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task A_client_is_invited_then_their_invitation_cancelled_each_with_their_address_typed()
    {
        var s = await ClientProjectAsync();
        var before = api.Emails.CountTo(s.ContactEmail);

        // an address that isn't a contact of the project's client is refused
        var (_, stranger) = await ProposeAsync(s.SuperAdmin, "propose_portal_invitation",
            new { project_id = s.Project.Id, step = "invite", email = AuthTestSteps.NewEmail("stranger") });
        stranger["message"]!.GetValue<string>().ShouldContain("isn't one of this project's client contacts");

        var (invite, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_invitation", new { project_id = s.Project.Id, step = "invite", email = s.ContactEmail });
        (invite.PendingAction!.Preview.Confirm, invite.PendingAction.Preview.Phrase, invite.PendingAction.Preview.Irreversible)
            .ShouldBe((ProtonConfirm.Typed, s.ContactEmail.ToLowerInvariant(), true));
        api.Emails.CountTo(s.ContactEmail).ShouldBe(before);
        await (await TryConfirmAsync(s.SuperAdmin, invite, "someone else")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);

        await ConfirmAsync(s.SuperAdmin, invite, s.ContactEmail.ToLowerInvariant());
        api.Emails.CountTo(s.ContactEmail).ShouldBe(before + 1);
        (await ContactAsync(s)).Status.ShouldBe(PortalClientStatus.Invited);

        // a waiting invitation is resent, not invited again
        var (again, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_invitation", new { project_id = s.Project.Id, step = "invite", email = s.ContactEmail });
        again.PendingAction.ShouldBeNull();

        var (cancel, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_end_access", new { project_id = s.Project.Id, step = "cancel_invitation", email = s.ContactEmail });
        cancel.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        await ConfirmAsync(s.SuperAdmin, cancel, s.ContactEmail.ToLowerInvariant());
        (await ContactAsync(s)).Status.ShouldNotBe(PortalClientStatus.Invited);
    }

    [Fact]
    public async Task A_resent_link_reaches_the_client_and_access_ends_only_for_someone_who_has_it()
    {
        var s = await ClientProjectAsync();
        (await Client.PostJsonAsync($"{Portal(s.Project.Id)}/invitations", new { email = s.ContactEmail }, s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var before = api.Emails.CountTo(s.ContactEmail);

        var (resend, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_invitation", new { project_id = s.Project.Id, step = "resend", email = s.ContactEmail });
        await ConfirmAsync(s.SuperAdmin, resend, s.ContactEmail.ToLowerInvariant());
        api.Emails.CountTo(s.ContactEmail).ShouldBe(before + 1);

        // no access yet to end
        var (_, notYet) = await ProposeAsync(s.SuperAdmin, "propose_portal_end_access", new { project_id = s.Project.Id, step = "end_access", email = s.ContactEmail });
        notYet["message"]!.GetValue<string>().ShouldContain("no access");

        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(s.ContactEmail), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase",
        });
        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());
        var name = (await ContactAsync(s)).Name.ShouldNotBeNull();

        var (end, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_end_access", new { project_id = s.Project.Id, step = "end_access", email = s.ContactEmail });
        (end.PendingAction!.Preview.Confirm, end.PendingAction.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, name));
        await ConfirmAsync(s.SuperAdmin, end, name);
        (await ContactAsync(s)).Status.ShouldBe(PortalClientStatus.AccessEnded);
    }

    [Fact]
    public async Task The_portal_drafts_main_pictures_and_introduction_change_only_on_a_second_step_and_not_for_a_non_image()
    {
        var s = await ClientProjectAsync();
        var first = await UploadAsync(s.SuperAdmin, s.Project.Id, "Living room.jpg", Jpeg);
        var second = await UploadAsync(s.SuperAdmin, s.Project.Id, "Kitchen.jpg", OtherJpeg);
        var pdf = await UploadPdfAsync(s.SuperAdmin, s.Project.Id, "Plan.pdf");

        var (_, notImage) = await ProposeAsync(s.SuperAdmin, "propose_portal_heroes", new { project_id = s.Project.Id, step = "add", item_id = pdf.Id });
        notImage["message"]!.GetValue<string>().ShouldContain("so it can't be a main picture");

        var (add, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_heroes", new { project_id = s.Project.Id, step = "add", item_id = first.Id });
        add.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        (await GetAsync<PortalContentResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/content")).Heroes.ShouldBeEmpty();
        await ConfirmAsync(s.SuperAdmin, add);
        await ConfirmAsync(s.SuperAdmin, (await ProposeAsync(s.SuperAdmin, "propose_portal_heroes", new { project_id = s.Project.Id, step = "add", item_id = second.Id })).Reply);

        var heroes = (await GetAsync<PortalContentResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/content")).Heroes;
        heroes.Select(h => h.Name).ShouldBe(["Living room.jpg", "Kitchen.jpg"]);

        var (order, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_heroes",
            new { project_id = s.Project.Id, step = "order", file_ids = new[] { heroes[1].FileId, heroes[0].FileId } });
        await ConfirmAsync(s.SuperAdmin, order);
        var content = await GetAsync<PortalContentResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/content");
        content.Heroes.Select(h => h.Name).ShouldBe(["Kitchen.jpg", "Living room.jpg"]);

        var (remove, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_heroes", new { project_id = s.Project.Id, step = "remove", file_id = heroes[0].FileId });
        await ConfirmAsync(s.SuperAdmin, remove);

        var summary = "Welcome to your project, Rita. " + Guid.NewGuid().ToString("N")[..6];
        var (intro, _) = await ProposeAsync(s.SuperAdmin, "propose_portal_summary", new { project_id = s.Project.Id, summary });
        intro.PendingAction!.Preview.Changes.Single().After.ShouldBe(summary);
        await ConfirmAsync(s.SuperAdmin, intro);
        var after = await GetAsync<PortalContentResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/content");
        (after.Summary, after.Heroes.Count).ShouldBe((summary, 1));
        (await ProposeAsync(s.SuperAdmin, "propose_portal_summary", new { project_id = s.Project.Id, summary })).Result["message"]!.GetValue<string>()
            .ShouldContain("already says that");
    }

    [Fact]
    public async Task The_discussion_with_the_client_is_written_edited_and_deleted_by_its_author_with_the_projects_name_typed()
    {
        var s = await ClientProjectAsync();
        var file = await UploadAsync(s.SuperAdmin, s.Project.Id, "Sketch.jpg", Jpeg);
        await ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, true);
        await PublishAsync(s.SuperAdmin, s.Project.Id);
        var body = $"  Here is the first sketch {Guid.NewGuid().ToString("N")[..6]}.";
        var messages = $"{Portal(s.Project.Id)}/discussion/messages";

        var (send, _) = await ProposeAsync(s.SuperAdmin, "propose_discussion_post", new { project_id = s.Project.Id, body });
        (send.PendingAction!.Preview.Confirm, send.PendingAction.Preview.Phrase, send.PendingAction.Preview.Irreversible)
            .ShouldBe((ProtonConfirm.Typed, s.Project.Title, true));
        (await GetAsync<PortalMessagePageResponse>(s.SuperAdmin, messages)).Messages.ShouldBeEmpty();
        await (await TryConfirmAsync(s.SuperAdmin, send, "wrong")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await ConfirmAsync(s.SuperAdmin, send, s.Project.Title);
        var posted = (await GetAsync<PortalMessagePageResponse>(s.SuperAdmin, messages)).Messages.Single();
        posted.Body.ShouldBe(body.Trim());
        posted.Author.IsYou.ShouldBeTrue();

        var (edit, _) = await ProposeAsync(s.SuperAdmin, "propose_discussion_change", new { project_id = s.Project.Id, kind = "message", step = "edit", id = posted.Id, body = "Corrected sketch." });
        edit.PendingAction.ShouldNotBeNull();
        await ConfirmAsync(s.SuperAdmin, edit, edit.PendingAction.Preview.Phrase);
        (await GetAsync<PortalMessagePageResponse>(s.SuperAdmin, messages)).Messages.Single().Body.ShouldBe("Corrected sketch.");

        var comment = "Please check the stairs.";
        var (onFile, _) = await ProposeAsync(s.SuperAdmin, "propose_discussion_post", new { project_id = s.Project.Id, body = comment, item_id = file.Id });
        await ConfirmAsync(s.SuperAdmin, onFile, s.Project.Title);
        var thread = await GetAsync<PortalCommentThreadResponse>(s.SuperAdmin, $"{Portal(s.Project.Id)}/discussion/comments/{file.Id}");
        thread.Comments.Single().Body.ShouldBe(comment);

        var (delete, _) = await ProposeAsync(s.SuperAdmin, "propose_discussion_change", new { project_id = s.Project.Id, kind = "message", step = "delete", id = posted.Id });
        delete.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        await ConfirmAsync(s.SuperAdmin, delete, s.Project.Title);
        (await GetAsync<PortalMessagePageResponse>(s.SuperAdmin, messages)).Messages.Single().Deleted.ShouldBeTrue();

        // not someone else's message, and not one that isn't there
        var (_, missing) = await ProposeAsync(s.SuperAdmin, "propose_discussion_change", new { project_id = s.Project.Id, kind = "message", step = "delete", id = posted.Id + 9999 });
        missing["message"]!.GetValue<string>().ShouldContain("no such message");
    }

    [Fact]
    public async Task Files_are_reordered_chosen_for_the_client_and_their_comments_edited_fixed_and_deleted()
    {
        var s = await ClientProjectAsync();
        var one = await UploadAsync(s.SuperAdmin, s.Project.Id, "One.jpg", Jpeg);
        var two = await UploadAsync(s.SuperAdmin, s.Project.Id, "Two.jpg", OtherJpeg);
        var files = $"/api/projects/{s.Project.Id}/files";

        var (_, same) = await ProposeAsync(s.SuperAdmin, "propose_reorder_files", new { project_id = s.Project.Id, section = "presentation", item_ids = new[] { one.Id, two.Id } });
        same["message"]!.GetValue<string>().ShouldContain("already in that order");
        var (reorder, _) = await ProposeAsync(s.SuperAdmin, "propose_reorder_files", new { project_id = s.Project.Id, section = "presentation", item_ids = new[] { two.Id, one.Id } });
        await ConfirmAsync(s.SuperAdmin, reorder);
        (await GetAsync<ProjectFilesResponse>(s.SuperAdmin, files)).Items.OrderBy(i => i.SortOrder).Select(i => i.Id).ShouldBe([two.Id, one.Id]);

        var (choose, _) = await ProposeAsync(s.SuperAdmin, "propose_file_for_client", new { project_id = s.Project.Id, item_id = one.Id, for_client = true });
        await ConfirmAsync(s.SuperAdmin, choose);
        (await GetAsync<ProjectFilesResponse>(s.SuperAdmin, files)).Items.Single(i => i.Id == one.Id).ForClient.ShouldBeTrue();

        var (post, _) = await ProposeAsync(s.SuperAdmin, "propose_comment_on_file", new { project_id = s.Project.Id, item_id = one.Id, body = "Too dark." });
        await ConfirmAsync(s.SuperAdmin, post);
        var comments = $"{files}/items/{one.Id}/comments";
        var comment = (await GetAsync<List<ProjectFileCommentResponse>>(s.SuperAdmin, comments)).Single();

        var (edit, _) = await ProposeAsync(s.SuperAdmin, "propose_edit_file_comment", new { project_id = s.Project.Id, item_id = one.Id, comment_id = comment.Id, body = "Too dark in the corner." });
        await ConfirmAsync(s.SuperAdmin, edit);
        var (fixedCard, _) = await ProposeAsync(s.SuperAdmin, "propose_set_comment_fixed", new { project_id = s.Project.Id, item_id = one.Id, comment_id = comment.Id, @fixed = true });
        await ConfirmAsync(s.SuperAdmin, fixedCard);
        var now = (await GetAsync<List<ProjectFileCommentResponse>>(s.SuperAdmin, comments)).Single();
        (now.Body, now.FixedAt.HasValue).ShouldBe(("Too dark in the corner.", true));

        var (delete, _) = await ProposeAsync(s.SuperAdmin, "propose_delete_file_comment", new { project_id = s.Project.Id, item_id = one.Id, comment_id = comment.Id });
        (delete.PendingAction!.Preview.Confirm, delete.PendingAction.Preview.Phrase).ShouldBe((ProtonConfirm.Typed, s.Project.Title));
        await ConfirmAsync(s.SuperAdmin, delete, s.Project.Title);
        (await GetAsync<List<ProjectFileCommentResponse>>(s.SuperAdmin, comments)).ShouldBeEmpty();

        // a colleague's comment isn't the user's to edit
        var (_, colleague) = await ProposeAsync(s.SuperAdmin, "propose_edit_file_comment", new { project_id = s.Project.Id, item_id = one.Id, comment_id = comment.Id + 9999, body = "x" });
        colleague["status"]!.GetValue<string>().ShouldNotBe("ok");
    }

    /// <summary>Comments are only on files the client sees, so the portal is published first.</summary>
    private async Task PublishAsync(AuthResponse staff, long projectId)
    {
        var revision = (await GetAsync<PortalContentResponse>(staff, $"{Portal(projectId)}/content")).Revision;
        var published = await Client.PostJsonAsync($"{Portal(projectId)}/content/publish", new { expectedRevision = revision }, staff.AccessToken);
        published.IsSuccessStatusCode.ShouldBeTrue(await published.Content.ReadAsStringAsync());
    }

    private async Task ChooseAsync(AuthResponse staff, long projectId, long itemId, bool forClient)
    {
        var response = await Client.PutJsonAsync($"/api/projects/{projectId}/files/items/{itemId}/for-client", new { forClient }, staff.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private Task<ProjectFileResponse> UploadPdfAsync(AuthResponse staff, long projectId, string fileName)
    {
        byte[] pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[100]];
        return UploadAsync(staff, projectId, fileName, pdf, "drawing");
    }

    private async Task<ProjectFileResponse> UploadAsync(AuthResponse staff, long projectId, string fileName, byte[] bytes, string section = "presentation")
    {
        var files = $"/api/projects/{projectId}/files";
        var start = await Client.PostJsonAsync($"{files}/uploads", new { clientToken = Guid.NewGuid().ToString(), section, fileName, size = bytes.Length }, staff.AccessToken);
        ((int)start.StatusCode).ShouldBe(201, await start.Content.ReadAsStringAsync());
        var started = await start.ReadAsync<PortalUploadStartedResponse>();
        var links = await (await Client.PostJsonAsync($"{files}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, staff.AccessToken))
            .ReadAsync<PortalPartLinksResponse>();
        (await Client.PutAsync(links.Parts[0].Url, new ByteArrayContent(bytes))).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await Client.PostEmptyAsync($"{files}/uploads/{started.UploadId}/complete", staff.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        return (await completed.ReadAsync<ProjectFileUploadCompletedResponse>()).Item!;
    }
}
