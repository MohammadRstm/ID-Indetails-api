using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class MessagingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    // Each test talks in teams of its own: the tests share one database, and a new company has no teams.
    private async Task<AuthResponse> InTeamAsync(AuthResponse superAdmin, string domain, long teamId)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, domain);
        await _steps.PutInTeamAsync(person.User.Id, teamId);
        return person;
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<MessageResponse> SendAsync(string path, object body, AuthResponse sender)
        => OkAsync<MessageResponse>(Client.PostJsonAsync($"/api/messages/{path}", body, sender.AccessToken));

    private Task<MessagesBadgeResponse> BadgeAsync(AuthResponse person)
        => OkAsync<MessagesBadgeResponse>(Client.GetAsync("/api/messages/badge", person.AccessToken));

    private Task<MessagePageResponse> PageAsync(string path, AuthResponse reader)
        => OkAsync<MessagePageResponse>(Client.GetAsync($"/api/messages/{path}", reader.AccessToken));

    [Fact]
    public async Task A_team_talks_in_its_chat_with_mentions_unread_counts_and_pages()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var engineering = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Engineering")).Id;
        var data = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Data")).Id;
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var engineer = await InTeamAsync(superAdmin, DomainCodes.Design, engineering);
        var teammate = await InTeamAsync(superAdmin, DomainCodes.Design, engineering);
        var analyst = await InTeamAsync(superAdmin, DomainCodes.Design, data);

        var first = await SendAsync($"teams/{engineering}", new { body = " Morning ", mentionIds = new[] { teammate.User.Id } }, engineer);
        first.Body.ShouldBe("Morning");
        first.Kind.ShouldBe(MessageKinds.Text);
        first.Mentions.ShouldHaveSingleItem().Id.ShouldBe(teammate.User.Id);

        (await BadgeAsync(teammate)).ShouldBe(new MessagesBadgeResponse(1, true));
        (await BadgeAsync(engineer)).ShouldBe(new MessagesBadgeResponse(0, false));
        var chat = (await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", teammate.AccessToken))).First();
        chat.Type.ShouldBe(ConversationType.Group);
        chat.Team!.Id.ShouldBe(engineering);
        chat.LastMessage!.Id.ShouldBe(first.Id);
        chat.Unread.ShouldBe(1);
        chat.UnreadMention.ShouldBeTrue();

        // The domain's admins and the super admins are in every team chat of it; other teams and domains aren't.
        (await PageAsync($"teams/{engineering}", designAdmin)).Items.ShouldHaveSingleItem().Id.ShouldBe(first.Id);
        (await PageAsync($"teams/{engineering}", superAdmin)).Items.ShouldHaveSingleItem();
        await (await Client.GetAsync($"/api/messages/teams/{engineering}", analyst.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/messages/teams/{engineering}", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var analystChats = await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", analyst.AccessToken));
        analystChats.ShouldHaveSingleItem().Team!.Id.ShouldBe(data);
        var members = (await OkAsync<List<ContactResponse>>(Client.GetAsync($"/api/messages/teams/{engineering}/members", engineer.AccessToken)))
            .Select(p => p.Id).ToList();
        members.ShouldContain(engineer.User.Id);
        members.ShouldContain(teammate.User.Id);
        members.ShouldContain(designAdmin.User.Id);
        members.ShouldContain(superAdmin.User.Id);
        members.ShouldNotContain(analyst.User.Id);
        members.ShouldNotContain(executionAdmin.User.Id);

        await (await Client.PostJsonAsync($"/api/messages/teams/{engineering}", new { body = "Hi", mentionIds = new[] { analyst.User.Id } }, engineer.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/messages/teams/{engineering}", new { body = "  " }, engineer.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await OkAsync<MessagesBadgeResponse>(Client.PostEmptyAsync($"/api/messages/teams/{engineering}/read", teammate.AccessToken)))
            .ShouldBe(new MessagesBadgeResponse(0, false));

        // Pages come oldest first, with older ones behind `before`.
        for (var i = 1; i <= 3; i++)
        {
            await SendAsync($"teams/{engineering}", new { body = $"Update {i}" }, teammate);
        }

        var latest = await PageAsync($"teams/{engineering}?limit=2", engineer);
        latest.Items.Select(m => m.Body).ShouldBe(["Update 2", "Update 3"]);
        latest.HasMore.ShouldBeTrue();
        var older = await PageAsync($"teams/{engineering}?before={latest.Items[0].Id}&limit=10", engineer);
        older.Items.Select(m => m.Body).ShouldBe(["Morning", "Update 1"]);
        older.HasMore.ShouldBeFalse();
        (await BadgeAsync(engineer)).Unread.ShouldBe(3);
    }

    [Fact]
    public async Task People_message_their_own_domain_and_the_super_admins_directly()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var engineering = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Engineering")).Id;
        var data = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Data")).Id;
        var engineer = await InTeamAsync(superAdmin, DomainCodes.Design, engineering);
        var analyst = await InTeamAsync(superAdmin, DomainCodes.Design, data);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var hello = await SendAsync($"direct/{analyst.User.Id}", new { body = "Got a minute?" }, engineer);
        var chat = (await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", analyst.AccessToken)))
            .Single(c => c.Type == ConversationType.Dm);
        chat.Person!.Id.ShouldBe(engineer.User.Id);
        chat.LastMessage!.Id.ShouldBe(hello.Id);
        chat.Unread.ShouldBe(1);
        (await PageAsync($"direct/{engineer.User.Id}", analyst)).Items.ShouldHaveSingleItem().Body.ShouldBe("Got a minute?");

        await SendAsync($"direct/{superAdmin.User.Id}", new { body = "Question" }, engineer);
        await SendAsync($"direct/{siteWorker.User.Id}", new { body = "Site visit" }, superAdmin);
        await (await Client.PostJsonAsync($"/api/messages/direct/{siteWorker.User.Id}", new { body = "Hi" }, engineer.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/messages/direct/{engineer.User.Id}", siteWorker.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/messages/direct/{engineer.User.Id}", new { body = "Me" }, engineer.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/messages/direct/{analyst.User.Id}", new { body = "Hi", mentionIds = new[] { analyst.User.Id } }, engineer.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var contacts = (await OkAsync<List<ContactResponse>>(Client.GetAsync("/api/messages/contacts", engineer.AccessToken))).Select(c => c.Id).ToList();
        contacts.ShouldContain(analyst.User.Id);
        contacts.ShouldContain(superAdmin.User.Id);
        contacts.ShouldNotContain(siteWorker.User.Id);
        contacts.ShouldNotContain(engineer.User.Id);

        // Nobody new can write to a deactivated person, but the history stays.
        (await Client.PostEmptyAsync($"/api/employees/{analyst.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/messages/direct/{analyst.User.Id}", new { body = "Still there?" }, engineer.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.UserDeactivated);
        (await PageAsync($"direct/{analyst.User.Id}", engineer)).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_comment_shared_to_the_team_chat_is_a_card_for_everyone_in_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designTeam = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Design")).Id;
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var engineer = await InTeamAsync(superAdmin, DomainCodes.Design, designTeam);
        var teammate = await InTeamAsync(superAdmin, DomainCodes.Design, designTeam);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var entry = await Work.LogAsync(engineer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(4), 90, "Stair details");

        var comment = await OkAsync<WorkEntryCommentResponse>(Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments",
            new { body = "Great detail on the landing", sendToGroup = true }, designAdmin.AccessToken));
        comment.SentToGroup.ShouldBeTrue();

        var card = (await PageAsync($"teams/{designTeam}", teammate)).Items.Single(m => m.Kind == MessageKinds.TaskComment);
        card.Sender.Id.ShouldBe(designAdmin.User.Id);
        card.Body.ShouldBe("Great detail on the landing");
        card.TaskComment!.EntryId.ShouldBe(entry.Id);
        card.TaskComment.CommentId.ShouldBe(comment.Id);
        card.TaskComment.Author.Id.ShouldBe(engineer.User.Id);
        card.TaskComment.Description.ShouldBe("Stair details");
        // a teammate doesn't learn how long a colleague worked; the author and whoever sees everyone's work do
        card.TaskComment.Minutes.ShouldBeNull();
        (await PageAsync($"teams/{designTeam}", engineer)).Items.Single(m => m.Kind == MessageKinds.TaskComment).TaskComment!.Minutes.ShouldBe(90);
        card.TaskComment.Attachment.ShouldBeNull();
        card.Sender.PhotoFileId.ShouldBeNull();
        card.Mentions.Select(p => p.Id).ShouldContain(teammate.User.Id);
        card.Mentions.Select(p => p.Id).ShouldNotContain(designAdmin.User.Id);
        (await BadgeAsync(teammate)).Mention.ShouldBeTrue();
    }

    [Fact]
    public async Task A_team_whose_chat_has_messages_is_kept()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var team = await OkAsync<TeamResponse>(Client.PostJsonAsync("/api/teams", new { name = $"Studio {Guid.NewGuid():N}"[..20] }, designAdmin.AccessToken));

        await SendAsync($"teams/{team.Id}", new { body = "Welcome" }, designAdmin);

        await (await Client.DeleteAsync($"/api/teams/{team.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.TeamHasChat);
    }
}
