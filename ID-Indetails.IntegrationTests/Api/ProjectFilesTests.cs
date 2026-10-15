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
/// A Design project's files (the Project files tab): everyone who can open the project sees them and adds more; the
/// uploader or a project files manager changes a file; managers order the sections and start the comments; only
/// client_portal:manage chooses what the client sees, and only the chosen files reach the preview and, once published,
/// the client. How a file travels to the content store is in <see cref="PortalContentTests"/>.
/// </summary>
public class ProjectFilesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE1, .. new byte[300]];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[100]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    private sealed record Setup(AuthResponse SuperAdmin, ProjectResponse Project, string ContactEmail);

    // ---- set-up ----

    /// <summary>A super admin and a Design project for a client with one email contact.</summary>
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
        return new Setup(superAdmin, await ProjectAsync(superAdmin, DomainCodes.Design, client.Id), contact);
    }

    private async Task<ProjectResponse> ProjectAsync(AuthResponse staff, string domain, long? clientId)
    {
        var title = "Project " + Guid.NewGuid().ToString("N")[..8];
        object body = clientId is null ? new { title, domainCode = domain, billingType = "internal" } : new { title, domainCode = domain, clientId };
        var response = await Client.PostJsonAsync("/api/projects", body, staff.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private async Task<AuthResponse> StaffAsync(Setup s, string role)
        => (await _steps.InviteAndAcceptAsync(s.SuperAdmin, role, DomainCodes.Design)).Auth;

    /// <summary>The project's client contact, invited and signed in to the portal: their access token.</summary>
    private async Task<string> ClientSignedInAsync(Setup s)
    {
        var invited = await Client.PostJsonAsync($"/api/projects/{s.Project.Id}/portal/invitations", new { email = s.ContactEmail }, s.SuperAdmin.AccessToken);
        invited.IsSuccessStatusCode.ShouldBeTrue(await invited.Content.ReadAsStringAsync());
        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(s.ContactEmail), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase",
        });
        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());
        return (await accepted.ReadAsync<PortalAuthResponse>()).AccessToken;
    }

    private async Task GrantAsync(Setup s, AuthResponse staff, string code)
    {
        var granted = await Client.PutJsonAsync($"/api/users/{staff.User.Id}/permissions",
            new { changes = new[] { new { code, action = "grant", reason = "Needed for this project" } } }, s.SuperAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
    }

    private static string Files(long projectId) => $"/api/projects/{projectId}/files";

    private static string Portal(long projectId) => $"/api/projects/{projectId}/portal/content";

    private Task<HttpResponseMessage> StartAsync(AuthResponse staff, long projectId, string fileName, long size, long? replaceItemId = null)
        => Client.PostJsonAsync($"{Files(projectId)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), section = "presentation", fileName, size, replaceItemId }, staff.AccessToken);

    /// <summary>Sends a whole (one-part) file and completes it: the new item, or the replaced one.</summary>
    private async Task<ProjectFileResponse> UploadAsync(
        AuthResponse staff, long projectId, string fileName, byte[] bytes, string section = "presentation", long? replaceItemId = null)
    {
        var start = await Client.PostJsonAsync($"{Files(projectId)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), section, fileName, size = bytes.Length, replaceItemId }, staff.AccessToken);
        ((int)start.StatusCode).ShouldBe(201, await start.Content.ReadAsStringAsync());
        var started = await start.ReadAsync<PortalUploadStartedResponse>();
        var links = await OkAsync<PortalPartLinksResponse>(
            Client.PostJsonAsync($"{Files(projectId)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, staff.AccessToken));
        (await Client.PutAsync(links.Parts[0].Url, new ByteArrayContent(bytes))).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await OkAsync<ProjectFileUploadCompletedResponse>(
            Client.PostEmptyAsync($"{Files(projectId)}/uploads/{started.UploadId}/complete", staff.AccessToken));
        return completed.Item!;
    }

    private Task<ProjectFilesResponse> FilesAsync(AuthResponse staff, long projectId)
        => OkAsync<ProjectFilesResponse>(Client.GetAsync(Files(projectId), staff.AccessToken));

    private Task<HttpResponseMessage> ChooseAsync(AuthResponse staff, long projectId, long itemId, bool forClient)
        => Client.PutJsonAsync($"{Files(projectId)}/items/{itemId}/for-client", new { forClient }, staff.AccessToken);

    private Task<HttpResponseMessage> RenameAsync(AuthResponse staff, long projectId, long itemId, string title)
        => Client.PutJsonAsync($"{Files(projectId)}/items/{itemId}", new { title }, staff.AccessToken);

    private Task<HttpResponseMessage> CommentAsync(AuthResponse staff, long projectId, long itemId, string body)
        => Client.PostJsonAsync($"{Files(projectId)}/items/{itemId}/comments", new { body }, staff.AccessToken);

    private Task<HttpResponseMessage> FixAsync(AuthResponse staff, long projectId, long commentId, bool done)
        => Client.PutJsonAsync($"{Files(projectId)}/comments/{commentId}/fixed", new { @fixed = done }, staff.AccessToken);

    private Task<PortalContentResponse> PortalAsync(AuthResponse staff, long projectId)
        => OkAsync<PortalContentResponse>(Client.GetAsync(Portal(projectId), staff.AccessToken));

    private async Task<PortalContentResponse> PublishAsync(AuthResponse staff, long projectId)
    {
        var revision = (await PortalAsync(staff, projectId)).Revision;
        return await OkAsync<PortalContentResponse>(Client.PostJsonAsync($"{Portal(projectId)}/publish", new { expectedRevision = revision }, staff.AccessToken));
    }

    private Task<HttpResponseMessage> AsClientAsync(string clientToken, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
        return Client.SendAsync(request);
    }

    private async Task<IReadOnlyList<string>> ClientSeesAsync(string clientToken, long projectId)
        => Titles(await OkAsync<PortalPresentationResponse>(AsClientAsync(clientToken, $"/api/portal/projects/{projectId}")));

    private static IReadOnlyList<string> Titles(PortalPresentationResponse presentation)
        => presentation.Sections.SelectMany(s => s.Items).Select(i => i.Title).ToList();

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<long> FileCommentNotificationsAsync(long userId)
        => _steps.ScalarAsync<long>(
            "SELECT COALESCE(SUM(`count`), 0) FROM notifications WHERE recipient_id = @u AND kind = @k",
            ("@u", userId), ("@k", NotificationKinds.ProjectFileComment));

    // ---- tests ----

    [Fact]
    public async Task An_employee_uploads_and_sees_all_the_projects_files()
    {
        var s = await ClientProjectAsync();
        var employee = await StaffAsync(s, RoleCodes.Employee);
        employee.User.Permissions.ShouldNotContain(PermissionCodes.ProjectFilesManage);

        var mine = await UploadAsync(employee, s.Project.Id, "Sketch.jpg", Jpeg);
        mine.UploadedBy!.Id.ShouldBe(employee.User.Id);
        mine.CanEdit.ShouldBeTrue();
        mine.ForClient.ShouldBeFalse();
        var theirs = await UploadAsync(s.SuperAdmin, s.Project.Id, "Plan.pdf", Pdf, "drawing");

        var seen = await FilesAsync(employee, s.Project.Id);
        seen.IsClientProject.ShouldBeTrue();
        seen.CanEdit.ShouldBeTrue();
        seen.CanManage.ShouldBeFalse();
        seen.CanChooseForClient.ShouldBeFalse();
        seen.Items.Select(i => i.Id).ShouldBe([mine.Id, theirs.Id], ignoreOrder: true);
        seen.Items.Single(i => i.Id == theirs.Id).CanEdit.ShouldBeFalse();
        (await Client.GetAsync($"{Files(s.Project.Id)}/items/{theirs.Id}/link", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // and everyone else on the project sees the employee's file
        (await FilesAsync(s.SuperAdmin, s.Project.Id)).Items.Single(i => i.Id == mine.Id).UploadedBy!.Id.ShouldBe(employee.User.Id);
    }

    [Fact]
    public async Task The_uploader_changes_their_file_another_employee_cant_and_a_manager_changes_anyones()
    {
        var s = await ClientProjectAsync();
        var uploader = await StaffAsync(s, RoleCodes.Employee);
        var colleague = await StaffAsync(s, RoleCodes.Employee);
        var manager = await StaffAsync(s, RoleCodes.Admin);
        manager.User.Permissions.ShouldContain(PermissionCodes.ProjectFilesManage);
        var file = await UploadAsync(uploader, s.Project.Id, "Sketch.jpg", Jpeg);

        // someone else's file: no rename, move, replace or delete
        await (await RenameAsync(colleague, s.Project.Id, file.Id, "Mine now")).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/items/{file.Id}", new { section = "model" }, colleague.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await StartAsync(colleague, s.Project.Id, "Sketch v2.jpg", Jpeg.Length, replaceItemId: file.Id)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"{Files(s.Project.Id)}/items/{file.Id}", colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await FilesAsync(colleague, s.Project.Id)).Items.Single().CanEdit.ShouldBeFalse();

        (await OkAsync<ProjectFileResponse>(RenameAsync(uploader, s.Project.Id, file.Id, "Sketch, first idea"))).Title.ShouldBe("Sketch, first idea");

        // a manager changes anyone's; a file they replace is theirs to change from then on
        var replaced = await UploadAsync(manager, s.Project.Id, "Sketch v2.jpg", OtherJpeg, replaceItemId: file.Id);
        replaced.Id.ShouldBe(file.Id);
        replaced.UploadedBy!.Id.ShouldBe(manager.User.Id);
        await (await RenameAsync(uploader, s.Project.Id, file.Id, "Back to mine")).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await OkAsync<ProjectFileResponse>(Client.PutJsonAsync($"{Files(s.Project.Id)}/items/{file.Id}", new { section = "model" }, manager.AccessToken)))
            .Section.ShouldBe(PortalSection.Model);
        (await Client.DeleteAsync($"{Files(s.Project.Id)}/items/{file.Id}", manager.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await FilesAsync(manager, s.Project.Id)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_project_file_managers_reorder_a_section()
    {
        var s = await ClientProjectAsync();
        var employee = await StaffAsync(s, RoleCodes.Employee);
        var manager = await StaffAsync(s, RoleCodes.Admin);
        var a = await UploadAsync(employee, s.Project.Id, "A.jpg", Jpeg);
        var b = await UploadAsync(employee, s.Project.Id, "B.jpg", Jpeg);

        // not even the uploader of every file in it
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order", new { itemIds = new[] { b.Id, a.Id } }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var reordered = await OkAsync<ProjectFilesResponse>(
            Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order", new { itemIds = new[] { b.Id, a.Id } }, manager.AccessToken));
        reordered.CanManage.ShouldBeTrue();
        reordered.Items.Select(i => i.Id).ShouldBe([b.Id, a.Id]);
    }

    [Fact]
    public async Task Managers_start_a_files_comments_anyone_replies_and_the_uploader_and_earlier_commenters_hear_of_them()
    {
        var s = await ClientProjectAsync();
        var uploader = await StaffAsync(s, RoleCodes.Employee);
        var colleague = await StaffAsync(s, RoleCodes.Employee);
        var manager = await StaffAsync(s, RoleCodes.Admin);
        var file = await UploadAsync(uploader, s.Project.Id, "Sketch.jpg", Jpeg);
        var thread = $"{Files(s.Project.Id)}/items/{file.Id}/comments";

        // nobody but a manager starts the comments, not even the file's uploader
        await (await CommentAsync(colleague, s.Project.Id, file.Id, "Is this final?")).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await CommentAsync(uploader, s.Project.Id, file.Id, "Thoughts?")).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await CommentAsync(manager, s.Project.Id, file.Id, "   ")).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await CommentAsync(manager, s.Project.Id, file.Id, new string('x', 2001))).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var first = await OkAsync<ProjectFileCommentResponse>(CommentAsync(manager, s.Project.Id, file.Id, "  The sofa should be darker.\r\n"));
        first.Body.ShouldBe("The sofa should be darker.");
        first.Author.Id.ShouldBe(manager.User.Id);
        first.CanEdit.ShouldBeTrue();
        first.CanFix.ShouldBeTrue();
        (await FileCommentNotificationsAsync(uploader.User.Id)).ShouldBe(1);

        // then anyone replies: the uploader and the earlier commenters hear of it, the author doesn't
        var reply = await OkAsync<ProjectFileCommentResponse>(CommentAsync(colleague, s.Project.Id, file.Id, "Darker how?"));
        reply.CanFix.ShouldBeFalse();
        (await FileCommentNotificationsAsync(uploader.User.Id)).ShouldBe(2);
        (await FileCommentNotificationsAsync(manager.User.Id)).ShouldBe(1);
        (await FileCommentNotificationsAsync(colleague.User.Id)).ShouldBe(0);
        // who and which file, never what was written
        var payload = await _steps.ScalarAsync<string>(
            "SELECT payload FROM notifications WHERE recipient_id = @u AND kind = @k", ("@u", manager.User.Id), ("@k", NotificationKinds.ProjectFileComment));
        payload.ShouldContain("Sketch");
        payload.ShouldNotContain("Darker how");

        // only its author edits or deletes a comment
        await (await Client.PutJsonAsync($"{Files(s.Project.Id)}/comments/{reply.Id}", new { body = "Mine now" }, uploader.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"{Files(s.Project.Id)}/comments/{first.Id}", colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var edited = await OkAsync<ProjectFileCommentResponse>(
            Client.PutJsonAsync($"{Files(s.Project.Id)}/comments/{reply.Id}", new { body = "Darker how? Walnut?" }, colleague.AccessToken));
        edited.Body.ShouldBe("Darker how? Walnut?");
        edited.EditedAt.ShouldNotBeNull();

        // the file's uploader or a manager marks a comment fixed, nobody else
        await (await FixAsync(colleague, s.Project.Id, first.Id, true)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var fixedComment = await OkAsync<ProjectFileCommentResponse>(FixAsync(uploader, s.Project.Id, first.Id, true));
        fixedComment.FixedAt.ShouldNotBeNull();
        fixedComment.FixedBy!.Id.ShouldBe(uploader.User.Id);

        var listed = await OkAsync<List<ProjectFileCommentResponse>>(Client.GetAsync(thread, colleague.AccessToken));
        listed.Select(c => c.Id).ShouldBe([first.Id, reply.Id]);
        listed.Single(c => c.Id == first.Id).CanFix.ShouldBeFalse();
        listed.Single(c => c.Id == reply.Id).CanDelete.ShouldBeTrue();
        (await FilesAsync(colleague, s.Project.Id)).Items.Single().Comments.ShouldBe(new ProjectFileCommentCountsResponse(2, 1));

        var reopened = await OkAsync<ProjectFileCommentResponse>(FixAsync(manager, s.Project.Id, first.Id, false));
        reopened.FixedAt.ShouldBeNull();
        reopened.FixedBy.ShouldBeNull();

        // deleting is for good
        (await Client.DeleteAsync($"{Files(s.Project.Id)}/comments/{reply.Id}", colleague.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OkAsync<List<ProjectFileCommentResponse>>(Client.GetAsync(thread, manager.AccessToken))).Select(c => c.Id).ShouldBe([first.Id]);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM project_file_comments WHERE id = @id", ("@id", reply.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Choosing_a_file_for_the_client_needs_client_portal_manage_which_admins_lack_by_default()
    {
        var s = await ClientProjectAsync();
        var employee = await StaffAsync(s, RoleCodes.Employee);
        var admin = await StaffAsync(s, RoleCodes.Admin);
        var file = await UploadAsync(admin, s.Project.Id, "Living room.jpg", Jpeg);

        // an admin manages the files, and sees the client portal through client access, but doesn't decide what the client sees
        (await FilesAsync(admin, s.Project.Id)).CanChooseForClient.ShouldBeFalse();
        await (await ChooseAsync(admin, s.Project.Id, file.Id, true)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await ChooseAsync(employee, s.Project.Id, file.Id, true)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await PortalAsync(admin, s.Project.Id)).CanManage.ShouldBeFalse();

        var chosen = await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, true));
        chosen.ForClient.ShouldBeTrue();
        chosen.ClientState.ShouldBe(PortalItemState.New);
        (await PortalAsync(admin, s.Project.Id)).Items.Single().Id.ShouldBe(file.Id);

        // granted client_portal:manage, they can
        await GrantAsync(s, admin, PermissionCodes.ClientPortalManage);
        (await FilesAsync(admin, s.Project.Id)).CanChooseForClient.ShouldBeTrue();
        (await OkAsync<ProjectFileResponse>(ChooseAsync(admin, s.Project.Id, file.Id, false))).ForClient.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_chosen_files_reach_the_preview_and_after_publishing_the_client()
    {
        var s = await ClientProjectAsync();
        var clientToken = await ClientSignedInAsync(s);
        var chosen = await UploadAsync(s.SuperAdmin, s.Project.Id, "Living room.jpg", Jpeg);
        var kept = await UploadAsync(s.SuperAdmin, s.Project.Id, "Rough sketch.jpg", OtherJpeg);
        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, chosen.Id, true));

        // the client portal tab and its preview show the chosen file only
        (await PortalAsync(s.SuperAdmin, s.Project.Id)).Items.Select(i => i.Id).ShouldBe([chosen.Id]);
        Titles(await OkAsync<PortalPresentationResponse>(Client.GetAsync($"{Portal(s.Project.Id)}/preview", s.SuperAdmin.AccessToken)))
            .ShouldBe(["Living room"]);
        await (await Client.GetAsync($"{Portal(s.Project.Id)}/items/{kept.Id}/link", s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await Client.GetAsync($"{Files(s.Project.Id)}/items/{kept.Id}/link", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await ClientSeesAsync(clientToken, s.Project.Id)).ShouldBeEmpty();

        await PublishAsync(s.SuperAdmin, s.Project.Id);

        (await ClientSeesAsync(clientToken, s.Project.Id)).ShouldBe(["Living room"]);
        (await AsClientAsync(clientToken, $"/api/portal/projects/{s.Project.Id}/items/{kept.Id}/link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var files = await FilesAsync(s.SuperAdmin, s.Project.Id);
        files.Items.Single(i => i.Id == chosen.Id).ClientState.ShouldBe(PortalItemState.Unchanged);
        files.Items.Single(i => i.Id == kept.Id).ClientState.ShouldBeNull();
    }

    [Fact]
    public async Task Unchoosing_and_publishing_hides_a_file_from_the_client_but_keeps_it_in_Project_files()
    {
        var s = await ClientProjectAsync();
        var clientToken = await ClientSignedInAsync(s);
        var file = await UploadAsync(s.SuperAdmin, s.Project.Id, "Living room.jpg", Jpeg);
        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, true));
        await PublishAsync(s.SuperAdmin, s.Project.Id);
        (await ClientSeesAsync(clientToken, s.Project.Id)).ShouldBe(["Living room"]);

        var unchosen = await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, false));
        unchosen.ClientState.ShouldBe(PortalItemState.Removed);
        var portal = await PortalAsync(s.SuperAdmin, s.Project.Id);
        portal.Items.Single().State.ShouldBe(PortalItemState.Removed);
        portal.Changes.Removed.ShouldBe(1);
        // clients keep it until the next publish
        (await ClientSeesAsync(clientToken, s.Project.Id)).ShouldBe(["Living room"]);

        await PublishAsync(s.SuperAdmin, s.Project.Id);

        (await ClientSeesAsync(clientToken, s.Project.Id)).ShouldBeEmpty();
        (await PortalAsync(s.SuperAdmin, s.Project.Id)).Items.ShouldBeEmpty();
        var still = (await FilesAsync(s.SuperAdmin, s.Project.Id)).Items.Single();
        still.Id.ShouldBe(file.Id);
        still.ForClient.ShouldBeFalse();
        still.Removed.ShouldBeFalse();
        still.ClientState.ShouldBeNull();
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_items WHERE id = @id AND published_file_id IS NULL", ("@id", file.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Discarding_puts_back_only_the_client_choices()
    {
        var s = await ClientProjectAsync();
        var shown = await UploadAsync(s.SuperAdmin, s.Project.Id, "A.jpg", Jpeg);
        var other = await UploadAsync(s.SuperAdmin, s.Project.Id, "B.jpg", OtherJpeg);
        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, shown.Id, true));
        await PublishAsync(s.SuperAdmin, s.Project.Id);

        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, shown.Id, false));
        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, other.Id, true));
        await OkAsync<ProjectFileResponse>(RenameAsync(s.SuperAdmin, s.Project.Id, shown.Id, "A, revised"));

        var revision = (await PortalAsync(s.SuperAdmin, s.Project.Id)).Revision;
        var discarded = await OkAsync<PortalContentResponse>(
            Client.PostJsonAsync($"{Portal(s.Project.Id)}/discard", new { expectedRevision = revision }, s.SuperAdmin.AccessToken));
        discarded.Items.Select(i => i.Id).ShouldBe([shown.Id]);

        // the choices are what clients see again; the rename belongs to Project files and stays, and nothing is deleted
        var files = await FilesAsync(s.SuperAdmin, s.Project.Id);
        files.Items.Count.ShouldBe(2);
        files.Items.Single(i => i.Id == shown.Id).ForClient.ShouldBeTrue();
        files.Items.Single(i => i.Id == shown.Id).Title.ShouldBe("A, revised");
        files.Items.Single(i => i.Id == other.Id).ForClient.ShouldBeFalse();
    }

    [Fact]
    public async Task An_internal_Design_project_has_files_but_nothing_for_a_client()
    {
        var s = await ClientProjectAsync();
        var employee = await StaffAsync(s, RoleCodes.Employee);
        var project = await ProjectAsync(s.SuperAdmin, DomainCodes.Design, null);

        var file = await UploadAsync(employee, project.Id, "Moodboard.jpg", Jpeg);
        var files = await FilesAsync(s.SuperAdmin, project.Id);
        files.IsClientProject.ShouldBeFalse();
        files.CanChooseForClient.ShouldBeFalse();
        files.Items.Single().Id.ShouldBe(file.Id);
        (await OkAsync<ProjectFileCommentResponse>(CommentAsync(s.SuperAdmin, project.Id, file.Id, "Lovely palette."))).ItemId.ShouldBe(file.Id);

        // there is no client to choose for, or to show a hero to
        await (await ChooseAsync(s.SuperAdmin, project.Id, file.Id, true)).ShouldFailWithAsync(422, ErrorCodes.PortalProjectNotEligible);
        await (await Client.PostJsonAsync($"{Files(project.Id)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), hero = true, fileName = "Cover.jpg", size = Jpeg.Length }, s.SuperAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PortalProjectNotEligible);
    }

    [Fact]
    public async Task An_archived_projects_files_are_read_only_but_can_still_be_opened()
    {
        var s = await ClientProjectAsync();
        var manager = await StaffAsync(s, RoleCodes.Admin);
        var file = await UploadAsync(manager, s.Project.Id, "Plan.pdf", Pdf);
        var comment = await OkAsync<ProjectFileCommentResponse>(CommentAsync(manager, s.Project.Id, file.Id, "Check the stairs."));
        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/archive", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var files = await FilesAsync(manager, s.Project.Id);
        files.CanEdit.ShouldBeFalse();
        files.ReadOnlyReason.ShouldNotBeNull();
        files.Items.Single().CanEdit.ShouldBeFalse();
        (await FilesAsync(s.SuperAdmin, s.Project.Id)).CanChooseForClient.ShouldBeFalse();

        Func<Task<HttpResponseMessage>>[] changes =
        [
            () => StartAsync(manager, s.Project.Id, "More.pdf", Pdf.Length),
            () => RenameAsync(manager, s.Project.Id, file.Id, "Renamed"),
            () => Client.DeleteAsync($"{Files(s.Project.Id)}/items/{file.Id}", manager.AccessToken),
            () => Client.PutJsonAsync($"{Files(s.Project.Id)}/sections/presentation/order", new { itemIds = new[] { file.Id } }, manager.AccessToken),
            () => ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, true),
            () => CommentAsync(manager, s.Project.Id, file.Id, "One more thing"),
            () => Client.PutJsonAsync($"{Files(s.Project.Id)}/comments/{comment.Id}", new { body = "Edited" }, manager.AccessToken),
            () => FixAsync(manager, s.Project.Id, comment.Id, true),
            () => Client.DeleteAsync($"{Files(s.Project.Id)}/comments/{comment.Id}", manager.AccessToken),
        ];
        foreach (var change in changes)
        {
            await (await change()).ShouldFailWithAsync(422, ErrorCodes.PortalContentReadOnly);
        }

        // still there to open, download and read
        (await Client.GetAsync($"{Files(s.Project.Id)}/items/{file.Id}/link", manager.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var thread = await OkAsync<List<ProjectFileCommentResponse>>(Client.GetAsync($"{Files(s.Project.Id)}/items/{file.Id}/comments", manager.AccessToken));
        thread.Single().CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task An_Execution_project_has_no_files_here()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var siteClient = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Site client",
            label = "Client",
            domainCode = DomainCodes.Execution,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("site") } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        var site = await ProjectAsync(superAdmin, DomainCodes.Execution, siteClient.Id);

        await (await Client.GetAsync(Files(site.Id), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await StartAsync(superAdmin, site.Id, "Plan.pdf", Pdf.Length)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"{Files(site.Id)}/items/1/comments", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Only_uploaders_of_files_chosen_for_the_client_take_part_in_its_discussion()
    {
        var s = await ClientProjectAsync();
        var employee = await StaffAsync(s, RoleCodes.Employee);
        var discussion = $"/api/projects/{s.Project.Id}/portal/discussion";
        var file = await UploadAsync(employee, s.Project.Id, "Sketch.jpg", Jpeg);

        // a file the client isn't to see doesn't bring its uploader into the client discussion
        (await Client.GetAsync(discussion, employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, true));
        (await Client.GetAsync(discussion, employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // taken back before it was ever published: out again
        await OkAsync<ProjectFileResponse>(ChooseAsync(s.SuperAdmin, s.Project.Id, file.Id, false));
        (await Client.GetAsync(discussion, employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
