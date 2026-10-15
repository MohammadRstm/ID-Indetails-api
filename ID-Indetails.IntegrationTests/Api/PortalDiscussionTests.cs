using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A client portal project's discussion: who takes part, the chat, comments on published files, mentions, edits,
/// read markers, notifications on both sides, and that nobody outside it (or outside the project) gets anything.
/// </summary>
public class PortalDiscussionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE1, .. new byte[300]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    private sealed record Setup(AuthResponse Staff, ProjectResponse Project, long ClientId, string ContactEmail, string ClientToken, long AccountId);

    // ---- set-up ----

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
        var project = await ProjectAsync(staff, client.Id);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/portal/invitations", new { email = contact }, staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var accepted = await (await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(contact), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase",
        })).ReadAsync<PortalAuthResponse>();
        return new Setup(staff, project, client.Id, contact, accepted.AccessToken, accepted.Account.Id);
    }

    private async Task<ProjectResponse> ProjectAsync(AuthResponse staff, long clientId)
    {
        var response = await Client.PostJsonAsync("/api/projects", new { title = "Project " + Guid.NewGuid().ToString("N")[..8], domainCode = DomainCodes.Design, clientId }, staff.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private static string Content(long projectId) => $"/api/projects/{projectId}/portal/content";

    private static string Staff(long projectId) => $"/api/projects/{projectId}/portal/discussion";

    private static string Mine(long projectId) => $"/api/portal/projects/{projectId}/discussion";

    /// <summary>Uploads a file and publishes the project, so clients see it.</summary>
    private async Task<long> PublishedFileAsync(Setup s, string name = "Living room.jpg", byte[]? bytes = null, long? replaceItemId = null)
    {
        var itemId = await DraftFileAsync(s, name, bytes, replaceItemId);
        await PublishAsync(s);
        return itemId;
    }

    /// <summary>Uploads a file to the draft only.</summary>
    private async Task<long> DraftFileAsync(Setup s, string name, byte[]? bytes = null, long? replaceItemId = null)
    {
        bytes ??= Jpeg;
        var started = await (await Client.PostJsonAsync($"{Content(s.Project.Id)}/uploads",
            new { clientToken = Guid.NewGuid().ToString(), section = "presentation", fileName = name, size = bytes.Length, replaceItemId },
            s.Staff.AccessToken)).ReadAsync<PortalUploadStartedResponse>();
        var links = await (await Client.PostJsonAsync($"{Content(s.Project.Id)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, s.Staff.AccessToken))
            .ReadAsync<PortalPartLinksResponse>();
        (await Client.PutAsync(links.Parts[0].Url, new ByteArrayContent(bytes))).IsSuccessStatusCode.ShouldBeTrue();
        var completed = await (await Client.PostEmptyAsync($"{Content(s.Project.Id)}/uploads/{started.UploadId}/complete", s.Staff.AccessToken))
            .ReadAsync<PortalUploadCompletedResponse>();
        return completed.Item!.Id;
    }

    private async Task PublishAsync(Setup s)
    {
        var revision = (await (await Client.GetAsync(Content(s.Project.Id), s.Staff.AccessToken)).ReadAsync<PortalContentResponse>()).Revision;
        var published = await Client.PostJsonAsync($"{Content(s.Project.Id)}/publish", new { expectedRevision = revision }, s.Staff.AccessToken);
        published.IsSuccessStatusCode.ShouldBeTrue(await published.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> AsClient(string token, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, options: ApiClientExtensions.Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private static object Message(string body, string[]? mentions = null, string? token = null)
        => new { body, clientToken = token ?? Guid.NewGuid().ToString(), mentions = mentions ?? [] };

    private Task<long> StaffNotificationsAsync(long userId, string kind)
        => _steps.ScalarAsync<long>("SELECT COALESCE(SUM(`count`), 0) FROM notifications WHERE recipient_id = @u AND kind = @k", ("@u", userId), ("@k", kind));

    // ---- tests ----

    [Fact]
    public async Task Staff_take_part_as_super_admins_managers_uploaders_or_people_who_logged_work_and_nobody_else()
    {
        var s = await ProjectWithClientAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Admin, DomainCodes.Execution);

        (await Client.GetAsync(Staff(s.Project.Id), s.Staff.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var outsider in new[] { admin, employee, executionAdmin })
        {
            (await Client.GetAsync(Staff(s.Project.Id), outsider.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // the manager takes part without uploading or logging anything
        await _steps.ExecuteAsync("UPDATE projects SET owner_id = @a WHERE id = @p", ("@a", admin.User.Id), ("@p", s.Project.Id));
        (await Client.GetAsync(Staff(s.Project.Id), admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // someone who logged work takes part
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) VALUES (@u, @p, 'Concept', '2026-10-01 08:00:00', '2026-10-01 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", employee.User.Id), ("@p", s.Project.Id));
        var summary = await OkAsync<DiscussionSummaryResponse>(Client.GetAsync(Staff(s.Project.Id), employee.AccessToken));
        summary.Participants.ShouldContain(p => p.Key == $"u{employee.User.Id}" && p.IsYou && p.Kind == "staff");
        summary.Participants.ShouldContain(p => p.Key == $"c{s.AccountId}" && p.Kind == "client");
        summary.Participants.ShouldNotContain(p => p.Key == $"u{executionAdmin.User.Id}");
    }

    [Fact]
    public async Task Someone_moved_to_Execution_no_longer_takes_part_in_a_Design_projects_discussion()
    {
        var s = await ProjectWithClientAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(s.Staff, RoleCodes.Employee, DomainCodes.Design);
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) VALUES (@u, @p, 'Concept', '2026-10-01 08:00:00', '2026-10-01 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", employee.User.Id), ("@p", s.Project.Id));
        (await Client.GetAsync(Staff(s.Project.Id), employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var moved = await Client.PutJsonAsync($"/api/employees/{employee.User.Id}/domain", new { domainCode = DomainCodes.Execution }, s.Staff.AccessToken);
        moved.IsSuccessStatusCode.ShouldBeTrue(await moved.Content.ReadAsStringAsync());

        // the work they logged stays, but the discussion is gone for them, as for anyone outside it
        (await Client.GetAsync(Staff(s.Project.Id), employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("Still here?"), employee.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var summary = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        summary.Participants.ShouldNotContain(p => p.Key == $"u{employee.User.Id}");
        summary.Participants.ShouldContain(p => p.Key == $"u{s.Staff.User.Id}");
        await (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("@Test employee?", [$"u{employee.User.Id}"])))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task The_client_and_the_team_chat_with_rolling_notifications_and_a_retry_is_never_sent_twice()
    {
        var s = await ProjectWithClientAsync();
        var token = Guid.NewGuid().ToString();

        var first = await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Hello <b>team</b>", token: token)));
        first.Body.ShouldBe("Hello <b>team</b>");
        first.Author.Kind.ShouldBe("client");
        first.Author.IsYou.ShouldBeTrue();
        var again = await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Hello <b>team</b>", token: token)));
        again.Id.ShouldBe(first.Id);
        await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Second one")));

        // the team has one rolling notification counting both messages, not three
        (await StaffNotificationsAsync(s.Staff.User.Id, NotificationKinds.PortalClientMessage)).ShouldBe(2);
        var staffView = await OkAsync<DiscussionSummaryResponse>(Client.GetAsync(Staff(s.Project.Id), s.Staff.AccessToken));
        staffView.UnreadMessages.ShouldBe(2);

        var reply = await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("Welcome!"), s.Staff.AccessToken));
        reply.Author.Kind.ShouldBe("staff");
        await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("Drawings tomorrow."), s.Staff.AccessToken));

        // the client: one notification counting both replies, without the text
        var bell = await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"));
        var note = bell.Items.Single();
        note.Kind.ShouldBe(PortalNotificationKind.TeamMessage);
        note.Count.ShouldBe(2);
        note.ProjectId.ShouldBe(s.Project.Id);
        note.Read.ShouldBeFalse();
        (await OkAsync<PortalUnreadResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications/unread-count"))).Unread.ShouldBe(1);
        (await OkAsync<PortalUnreadResponse>(AsClient(s.ClientToken, HttpMethod.Post, "/api/portal/notifications/read", new { all = true }))).Unread.ShouldBe(0);

        // the chat, oldest first, and the read marker
        var page = await OkAsync<PortalMessagePageResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/messages"));
        page.Messages.Select(m => m.Body).ShouldBe(["Hello <b>team</b>", "Second one", "Welcome!", "Drawings tomorrow."]);
        page.Messages.Where(m => m.Author.Kind == "staff").ShouldAllBe(m => !m.CanEdit);
        var summary = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        summary.UnreadMessages.ShouldBe(2);
        var read = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/read", new { lastMessageId = summary.LastMessageId }));
        read.UnreadMessages.ShouldBe(0);
        // a marker never goes back
        (await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/read", new { lastMessageId = first.Id }))).UnreadMessages.ShouldBe(0);
    }

    [Fact]
    public async Task The_chat_is_paged_from_the_newest_back()
    {
        var s = await ProjectWithClientAsync();
        for (var i = 1; i <= 33; i++)
        {
            await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message($"Message {i}"), s.Staff.AccessToken));
        }

        var newest = await OkAsync<PortalMessagePageResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/messages"));
        newest.Messages.Count.ShouldBe(30);
        newest.HasMore.ShouldBeTrue();
        newest.Messages[^1].Body.ShouldBe("Message 33");
        var earlier = await OkAsync<PortalMessagePageResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/messages?before={newest.Messages[0].Id}"));
        earlier.Messages.Select(m => m.Body).ShouldBe(["Message 1", "Message 2", "Message 3"]);
        earlier.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task Mentions_reach_the_person_once_and_only_people_taking_part_can_be_mentioned()
    {
        var s = await ProjectWithClientAsync();
        var staffKey = $"u{s.Staff.User.Id}";

        await (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Hi @Nobody", ["u999999"])))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var message = await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Can you check this?")));
        (await StaffNotificationsAsync(s.Staff.User.Id, NotificationKinds.PortalMention)).ShouldBe(0);

        // an edit that adds the mention notifies; editing it again doesn't notify twice
        var edited = await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Put, $"{Mine(s.Project.Id)}/messages/{message.Id}",
            new { body = "Can you check this, @Test super_admin?", mentions = new[] { staffKey } }));
        edited.Mentions.ShouldBe([staffKey]);
        edited.EditedAt.ShouldNotBeNull();
        await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Put, $"{Mine(s.Project.Id)}/messages/{message.Id}",
            new { body = "Can you check this please, @Test super_admin?", mentions = new[] { staffKey } }));
        (await StaffNotificationsAsync(s.Staff.User.Id, NotificationKinds.PortalMention)).ShouldBe(1);

        // only its author changes a message
        await (await Client.PutJsonAsync($"{Staff(s.Project.Id)}/messages/{message.Id}", new { body = "Changed", mentions = Array.Empty<string>() }, s.Staff.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // the team mentions the client: a mention, not also the rolling chat notification
        await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("@Rita Frem done", [$"c{s.AccountId}"]), s.Staff.AccessToken));
        var bell = await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"));
        bell.Items.Single().Kind.ShouldBe(PortalNotificationKind.Mention);
    }

    [Fact]
    public async Task Clients_comment_on_published_files_only_and_comments_keep_to_the_file_they_were_about()
    {
        var s = await ProjectWithClientAsync();
        var itemId = await PublishedFileAsync(s);

        // a draft file can't be commented on, or even found, by the client or the team
        var draft = await DraftFileAsync(s, "Draft.jpg");
        await (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/comments",
            new { itemId = draft, body = "Nice", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() })).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/comments/{draft}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await (await Client.PostJsonAsync($"{Staff(s.Project.Id)}/comments",
            new { itemId = draft, body = "Internal", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }, s.Staff.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var comment = await OkAsync<PortalCommentResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/comments",
            new { itemId, body = "Can the sofa be darker?", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }));
        comment.FileName.ShouldBe("Living room.jpg");
        (await StaffNotificationsAsync(s.Staff.User.Id, NotificationKinds.PortalClientComment)).ShouldBe(1);

        await OkAsync<PortalCommentResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/comments",
            new { itemId, body = "Sure, new version tomorrow.", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }, s.Staff.AccessToken));
        (await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"))).Items.Single().Kind
            .ShouldBe(PortalNotificationKind.TeamComment);

        // the file is replaced and published: the earlier comments say so
        await PublishedFileAsync(s, "Living room v2.jpg", OtherJpeg, replaceItemId: itemId);
        var thread = await OkAsync<PortalCommentThreadResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/comments/{itemId}"));
        thread.Available.ShouldBeTrue();
        thread.FileName.ShouldBe("Living room v2.jpg");
        thread.Comments.ShouldAllBe(c => c.OnEarlierVersion && c.FileName == "Living room.jpg");

        // the file is removed and published: the thread stays readable, closed, and attached to nothing else
        (await Client.DeleteAsync($"{Content(s.Project.Id)}/items/{itemId}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await PublishAsync(s);
        var closed = await OkAsync<PortalCommentThreadResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/comments/{itemId}"));
        closed.Available.ShouldBeFalse();
        closed.Comments.Count.ShouldBe(2);
        closed.Comments.ShouldAllBe(c => !c.CanEdit);
        await (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/comments",
            new { itemId, body = "More?", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() })).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)))).Threads.Single().Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Each_files_comments_are_read_on_their_own()
    {
        var s = await ProjectWithClientAsync();
        var living = await PublishedFileAsync(s);
        var kitchen = await PublishedFileAsync(s, "Kitchen.jpg", OtherJpeg);
        async Task<PortalCommentResponse> TeamCommentsAsync(long itemId, string body) => await OkAsync<PortalCommentResponse>(Client.PostJsonAsync(
            $"{Staff(s.Project.Id)}/comments", new { itemId, body, clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }, s.Staff.AccessToken));
        await TeamCommentsAsync(living, "Darker sofa?");
        var newest = await TeamCommentsAsync(living, "Or lighter walls?");
        var marble = await TeamCommentsAsync(kitchen, "Marble or wood?");
        Task<DiscussionSummaryResponse> ReadAsync(object request)
            => OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/read", request));

        // each thread's size, its file and how much is new, newest thread first
        var before = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        before.Threads.Select(t => (t.ItemId, t.Title, t.FileName, t.Available, t.Count, t.Unread))
            .ShouldBe([(kitchen, "Kitchen", "Kitchen.jpg", true, 1, 1), (living, "Living room", "Living room.jpg", true, 2, 2)]);
        before.Threads[0].LastAt.ShouldBe(marble.CreatedAt);
        before.UnreadComments.ShouldBe(3);
        before.LastCommentId.ShouldBe(marble.Id);

        // reading one file's comments leaves the other file's new
        var read = await ReadAsync(new { itemId = living, lastCommentId = newest.Id });
        read.Threads.Single(t => t.ItemId == living).Unread.ShouldBe(0);
        read.Threads.Single(t => t.ItemId == kitchen).Unread.ShouldBe(1);
        read.UnreadComments.ShouldBe(1);
        (await OkAsync<PortalAccountResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/me"))).Projects.Single().Unread.ShouldBe(1);

        // a marker never passes the file's own newest comment, so what comes later is new
        await ReadAsync(new { itemId = living, lastCommentId = newest.Id + 1000 });
        await TeamCommentsAsync(living, "One more idea");
        (await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id))))
            .Threads.Single(t => t.ItemId == living).Unread.ShouldBe(1);

        // a comment marker needs its file
        (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/read", new { lastCommentId = newest.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the reader's own comments and deleted ones are never new, but count in their thread
        await OkAsync<PortalCommentResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/comments",
            new { itemId = kitchen, body = "Wood, please.", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }));
        var withdrawn = await TeamCommentsAsync(kitchen, "Or stone?");
        (await Client.DeleteAsync($"{Staff(s.Project.Id)}/comments/{withdrawn.Id}", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var after = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        var kitchenThread = after.Threads.Single(t => t.ItemId == kitchen);
        kitchenThread.Count.ShouldBe(3);
        kitchenThread.Unread.ShouldBe(1);
        after.Threads[0].ItemId.ShouldBe(kitchen);
        after.LastCommentId.ShouldBe(withdrawn.Id);
        after.UnreadComments.ShouldBe(2);
    }

    [Fact]
    public async Task Reading_in_several_tabs_at_once_keeps_one_marker_each()
    {
        var s = await ProjectWithClientAsync();
        var itemId = await PublishedFileAsync(s);
        var message = await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("Drawings tomorrow."), s.Staff.AccessToken));
        var comment = await OkAsync<PortalCommentResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/comments",
            new { itemId, body = "Darker sofa?", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }, s.Staff.AccessToken));

        // the client's first markers, written by three tabs at the same moment
        var read = new { lastMessageId = message.Id, itemId, lastCommentId = comment.Id };
        var tabs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/read", read)));
        foreach (var response in tabs)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        }

        var summary = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        summary.UnreadMessages.ShouldBe(0);
        summary.UnreadComments.ShouldBe(0);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_reads WHERE project_id = @p AND account_id = @a", ("@p", s.Project.Id), ("@a", s.AccountId)))
            .ShouldBe(1);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_comment_reads WHERE item_id = @i AND account_id = @a", ("@i", itemId), ("@a", s.AccountId)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Team_messages_sent_at_once_make_one_client_notification_counting_them_all()
    {
        var s = await ProjectWithClientAsync();

        var sent = await Task.WhenAll(Enumerable.Range(1, 2).Select(i =>
            Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message($"Update {i}"), s.Staff.AccessToken)));
        foreach (var response in sent)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        }

        var note = (await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"))).Items.ShouldHaveSingleItem();
        note.Kind.ShouldBe(PortalNotificationKind.TeamMessage);
        note.Count.ShouldBe(2);
        note.Read.ShouldBeFalse();

        // once read, the next message starts it again at 1
        await OkAsync<PortalUnreadResponse>(AsClient(s.ClientToken, HttpMethod.Post, "/api/portal/notifications/read", new { all = true }));
        await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("One more"), s.Staff.AccessToken));
        var again = (await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"))).Items.ShouldHaveSingleItem();
        again.Id.ShouldBe(note.Id);
        again.Count.ShouldBe(1);
        again.Read.ShouldBeFalse();
    }

    [Fact]
    public async Task Authors_edit_or_delete_their_own_comments_and_a_deleted_one_keeps_its_place()
    {
        var s = await ProjectWithClientAsync();
        var itemId = await PublishedFileAsync(s);
        var comment = await OkAsync<PortalCommentResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/comments",
            new { itemId, body = "First thought", clientToken = Guid.NewGuid().ToString(), mentions = Array.Empty<string>() }));

        await (await Client.DeleteAsync($"{Staff(s.Project.Id)}/comments/{comment.Id}", s.Staff.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var edited = await OkAsync<PortalCommentResponse>(AsClient(s.ClientToken, HttpMethod.Put, $"{Mine(s.Project.Id)}/comments/{comment.Id}",
            new { body = "Second thought", mentions = Array.Empty<string>() }));
        edited.Body.ShouldBe("Second thought");
        edited.EditedAt.ShouldNotBeNull();

        var deleted = await OkAsync<PortalCommentResponse>(AsClient(s.ClientToken, HttpMethod.Delete, $"{Mine(s.Project.Id)}/comments/{comment.Id}"));
        deleted.Deleted.ShouldBeTrue();
        deleted.Body.ShouldBeNull();
        var thread = await OkAsync<PortalCommentThreadResponse>(Client.GetAsync($"{Staff(s.Project.Id)}/comments/{itemId}", s.Staff.AccessToken));
        thread.Comments.Single().Deleted.ShouldBeTrue();
    }

    [Fact]
    public async Task An_archived_projects_discussion_can_be_read_but_not_written_and_a_completed_ones_works()
    {
        var s = await ProjectWithClientAsync();
        var message = await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Before")));

        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/complete", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await OkAsync<PortalChatMessageResponse>(AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Still open after completion")));

        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/archive", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var summary = await OkAsync<DiscussionSummaryResponse>(AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id)));
        summary.CanWrite.ShouldBeFalse();
        summary.ClosedReason.ShouldNotBeNull();
        (await OkAsync<PortalMessagePageResponse>(AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(s.Project.Id)}/messages"))).Messages.Count.ShouldBe(2);
        await (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("After"))).ShouldFailWithAsync(422, ErrorCodes.PortalDiscussionClosed);
        await (await AsClient(s.ClientToken, HttpMethod.Put, $"{Mine(s.Project.Id)}/messages/{message.Id}", new { body = "Changed", mentions = Array.Empty<string>() }))
            .ShouldFailWithAsync(422, ErrorCodes.PortalDiscussionClosed);
        await (await Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("After"), s.Staff.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.PortalDiscussionClosed);
    }

    [Fact]
    public async Task Nobody_reaches_a_discussion_they_dont_take_part_in()
    {
        var s = await ProjectWithClientAsync();
        await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("For the client"), s.Staff.AccessToken));

        // the two sign-ins never cross
        (await AsClient(s.ClientToken, HttpMethod.Get, Staff(s.Project.Id))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync(Mine(s.Project.Id), s.Staff.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync("/api/portal/notifications", s.Staff.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // another project of the same client, without access: nothing
        var other = await ProjectAsync(s.Staff, s.ClientId);
        await OkAsync<PortalChatMessageResponse>(Client.PostJsonAsync($"{Staff(other.Id)}/messages", Message("Secret"), s.Staff.AccessToken));
        (await AsClient(s.ClientToken, HttpMethod.Get, Mine(other.Id))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AsClient(s.ClientToken, HttpMethod.Get, $"{Mine(other.Id)}/messages")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // access ended: the discussion and its notifications are gone at once
        (await OkAsync<PortalUnreadResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications/unread-count"))).Unread.ShouldBe(1);
        var clients = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/clients", s.Staff.AccessToken)).ReadAsync<PortalClientsResponse>();
        var accessId = clients.Clients.Single(c => c.Email == s.ContactEmail).AccessId!.Value;
        // a second project keeps the client signed in, so the refusals below are about this project
        (await Client.PostJsonAsync($"/api/projects/{other.Id}/portal/invitations", new { email = s.ContactEmail }, s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{s.Project.Id}/portal/access/{accessId}/revoke", s.Staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await AsClient(s.ClientToken, HttpMethod.Get, Mine(s.Project.Id))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AsClient(s.ClientToken, HttpMethod.Post, $"{Mine(s.Project.Id)}/messages", Message("Still here?"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OkAsync<PortalNotificationPageResponse>(AsClient(s.ClientToken, HttpMethod.Get, "/api/portal/notifications"))).Items
            .ShouldNotContain(n => n.ProjectId == s.Project.Id);

        // the client is no longer someone the team can mention
        await (await Client.PostJsonAsync($"{Staff(s.Project.Id)}/messages", Message("@Rita", [$"c{s.AccountId}"]), s.Staff.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
