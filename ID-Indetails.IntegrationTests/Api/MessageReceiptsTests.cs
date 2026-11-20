using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Ticks, "Seen by" and last seen in Messages (20 November 2026).</summary>
public class MessageReceiptsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    // The tests share one database, so team chats and Announcements hold other tests' people (their super admins
    // above all): look for this test's people, never at totals.
    private async Task<AuthResponse> InTeamAsync(AuthResponse superAdmin, long teamId)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
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

    private async Task<MessageResponse> InPageAsync(string path, long messageId, AuthResponse reader)
        => (await OkAsync<MessagePageResponse>(Client.GetAsync($"/api/messages/{path}", reader.AccessToken))).Items.Single(m => m.Id == messageId);

    private Task<MessageReceiptsResponse> ReceiptsAsync(long messageId, AuthResponse viewer)
        => OkAsync<MessageReceiptsResponse>(Client.GetAsync($"/api/messages/{messageId}/receipts", viewer.AccessToken));

    [Fact]
    public async Task A_direct_message_goes_from_sent_to_received_to_read_and_only_its_sender_sees_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var studio = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Studio")).Id;
        var engineer = await InTeamAsync(superAdmin, studio);
        var analyst = await InTeamAsync(superAdmin, studio);

        var hello = await SendAsync($"direct/{analyst.User.Id}", new { body = "Got a minute?" }, engineer);
        hello.Receipt.ShouldBe(new MessageReceiptResponse(ReceiptStatus.Sent, 0, 0, 1));
        (await InPageAsync($"direct/{engineer.User.Id}", hello.Id, analyst)).Receipt.ShouldBeNull();

        // their device loads the badge: received, and the sender hears of it
        await BadgeAsync(analyst);
        (await InPageAsync($"direct/{analyst.User.Id}", hello.Id, engineer)).Receipt.ShouldBe(new MessageReceiptResponse(ReceiptStatus.Delivered, 0, 1, 1));
        api.Live.SentTo(engineer.User.Id, LiveSignals.Receipts).ShouldContain(new LiveChat(null, analyst.User.Id));

        // read: blue, with the time, since it is the last message they read
        (await Client.PostEmptyAsync($"/api/messages/direct/{engineer.User.Id}/read", analyst.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await InPageAsync($"direct/{analyst.User.Id}", hello.Id, engineer)).Receipt.ShouldBe(new MessageReceiptResponse(ReceiptStatus.Read, 1, 1, 1));
        var receipts = await ReceiptsAsync(hello.Id, engineer);
        receipts.Seen.ShouldHaveSingleItem().Id.ShouldBe(analyst.User.Id);
        receipts.Seen[0].At.ShouldNotBeNull();
        receipts.Delivered.ShouldBeEmpty();
        receipts.NotReceived.ShouldBeEmpty();
        var chat = (await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", engineer.AccessToken)))
            .Single(c => c.Type == ConversationType.Dm && c.Person!.Id == analyst.User.Id);
        chat.LastMessage!.Receipt!.Status.ShouldBe(ReceiptStatus.Read);

        // the recipient doesn't see who read what
        await (await Client.GetAsync($"/api/messages/{hello.Id}/receipts", analyst.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // last seen comes with each person (signing in records it); nobody has the app open in a test
        chat.Person!.LastSeenAt.ShouldNotBeNull();
        chat.Person.Online.ShouldBeFalse();
        (await OkAsync<List<ContactResponse>>(Client.GetAsync("/api/messages/contacts", engineer.AccessToken)))
            .Single(c => c.Id == analyst.User.Id).LastSeenAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task In_a_team_chat_the_sender_and_the_super_admins_see_who_has_seen_a_message()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var atelier = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Atelier")).Id;
        var other = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Elsewhere")).Id;
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var engineer = await InTeamAsync(superAdmin, atelier);
        var teammate = await InTeamAsync(superAdmin, atelier);
        var outsider = await InTeamAsync(superAdmin, other);

        var plans = await SendAsync($"teams/{atelier}", new { body = "Plans are up" }, engineer);
        plans.Receipt!.Status.ShouldBe(ReceiptStatus.Sent);
        plans.Receipt.Seen.ShouldBe(0);

        (await Client.PostEmptyAsync($"/api/messages/teams/{atelier}/read", teammate.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var receipts = await ReceiptsAsync(plans.Id, engineer);
        receipts.Seen.ShouldContain(p => p.Id == teammate.User.Id && p.At != null);
        receipts.NotReceived.ShouldContain(p => p.Id == designAdmin.User.Id);
        receipts.Seen.Concat(receipts.Delivered).Concat(receipts.NotReceived).ShouldNotContain(p => p.Id == engineer.User.Id);
        receipts.Summary.Status.ShouldBe(ReceiptStatus.Sent);
        api.Live.SentTo(engineer.User.Id, LiveSignals.Receipts).ShouldContain(new LiveChat(atelier, null));

        // the admin's device receives it
        await BadgeAsync(designAdmin);
        (await ReceiptsAsync(plans.Id, engineer)).Delivered.ShouldContain(p => p.Id == designAdmin.User.Id);

        // a teammate doesn't see who read someone else's message; a super admin does
        (await InPageAsync($"teams/{atelier}", plans.Id, teammate)).Receipt.ShouldBeNull();
        await (await Client.GetAsync($"/api/messages/{plans.Id}/receipts", teammate.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await InPageAsync($"teams/{atelier}", plans.Id, superAdmin)).Receipt.ShouldNotBeNull();
        (await ReceiptsAsync(plans.Id, superAdmin)).Seen.ShouldContain(p => p.Id == teammate.User.Id);

        // someone not in the chat can't find the message at all
        await (await Client.GetAsync($"/api/messages/{plans.Id}/receipts", outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/messages/999999999/receipts", engineer.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Whoever_announces_sees_when_each_person_read_an_announcement_and_readers_see_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var news = await SendAsync("announcements", new { body = "The office is closed on Friday" }, superAdmin);
        (await Client.PostEmptyAsync("/api/messages/announcements/read", designer.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // a second one, read later: the first keeps the time it was read
        var more = await SendAsync("announcements", new { body = "And Monday" }, superAdmin);
        (await Client.PostEmptyAsync("/api/messages/announcements/read", designer.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var first = await ReceiptsAsync(news.Id, superAdmin);
        var second = await ReceiptsAsync(more.Id, superAdmin);
        var seenFirst = first.Seen.Single(p => p.Id == designer.User.Id).At.ShouldNotBeNull();
        var seenSecond = second.Seen.Single(p => p.Id == designer.User.Id).At.ShouldNotBeNull();
        seenFirst.ShouldBeLessThanOrEqualTo(seenSecond);
        first.NotReceived.ShouldContain(p => p.Id == siteWorker.User.Id);
        api.Live.SentTo(superAdmin.User.Id, LiveSignals.Receipts).ShouldContain(LiveChat.ForAnnouncements);

        // readers see no receipts
        (await InPageAsync("announcements", news.Id, designer)).Receipt.ShouldBeNull();
        await (await Client.GetAsync($"/api/messages/{news.Id}/receipts", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // someone who joined afterwards isn't counted
        var (_, _, newcomer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var later = await ReceiptsAsync(news.Id, superAdmin);
        later.Seen.Concat(later.Delivered).Concat(later.NotReceived).ShouldNotContain(p => p.Id == newcomer.User.Id);
    }
}
