using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Custom chat groups: made and managed by super admins (<c>message:manage_groups</c>), read and written by their members.</summary>
public class ChatGroupTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<AuthResponse> PersonAsync(AuthResponse superAdmin, string role, string domain)
        => (await _steps.InviteAndAcceptAsync(superAdmin, role, domain)).Item3;

    private Task<ChatGroupDetailResponse> CreateAsync(AuthResponse superAdmin, string name, params long[] memberIds)
        => OkAsync<ChatGroupDetailResponse>(Client.PostJsonAsync("/api/messages/groups", new { name, memberIds }, superAdmin.AccessToken));

    private Task<MessagePageResponse> PageAsync(long groupId, AuthResponse reader)
        => OkAsync<MessagePageResponse>(Client.GetAsync($"/api/messages/groups/{groupId}", reader.AccessToken));

    private async Task<ConversationResponse?> InboxEntryAsync(long groupId, AuthResponse person)
        => (await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", person.AccessToken)))
            .SingleOrDefault(c => c.Group?.Id == groupId);

    [Fact]
    public async Task A_super_admin_makes_a_group_of_any_staff_who_talk_in_it_and_nobody_else_reads_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designer = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var siteEngineer = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var websiteAdmin = await PersonAsync(superAdmin, RoleCodes.Admin, DomainCodes.Website);
        var outsider = await PersonAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        // across businesses, and the creator is in it without picking themselves
        var group = await CreateAsync(superAdmin, "  Site leads  ", designer.User.Id, siteEngineer.User.Id, websiteAdmin.User.Id);
        group.Name.ShouldBe("Site leads");
        group.IsMember.ShouldBeTrue();
        group.Members.Select(m => m.Id).ShouldBe(
            [superAdmin.User.Id, designer.User.Id, siteEngineer.User.Id, websiteAdmin.User.Id], ignoreOrder: true);

        // the grey line saying who made it, which never counts as unread
        var opening = (await PageAsync(group.Id, designer)).Items.ShouldHaveSingleItem();
        opening.Kind.ShouldBe(MessageKinds.GroupEvent);
        opening.Body.ShouldBe("created the group “Site leads”");
        opening.Sender.Id.ShouldBe(superAdmin.User.Id);
        var entry = (await InboxEntryAsync(group.Id, designer))!;
        entry.Type.ShouldBe(ConversationType.CustomGroup);
        entry.Group!.MemberCount.ShouldBe(4);
        entry.Unread.ShouldBe(0);

        var hello = await OkAsync<MessageResponse>(Client.PostJsonAsync(
            $"/api/messages/groups/{group.Id}", new { body = "Pour on Monday", mentionIds = new[] { siteEngineer.User.Id } }, designer.AccessToken));
        hello.Receipt.ShouldNotBeNull();
        (await OkAsync<MessagesBadgeResponse>(Client.GetAsync("/api/messages/badge", siteEngineer.AccessToken))).ShouldBe(new MessagesBadgeResponse(1, true));
        (await InboxEntryAsync(group.Id, websiteAdmin))!.Unread.ShouldBe(1);
        // the super admin in it sees everyone's ticks, as in a team chat
        (await PageAsync(group.Id, superAdmin)).Items.Last().Receipt!.Of.ShouldBe(3);
        (await PageAsync(group.Id, websiteAdmin)).Items.Last().Receipt.ShouldBeNull();

        (await OkAsync<MessagesBadgeResponse>(Client.PostEmptyAsync($"/api/messages/groups/{group.Id}/read", siteEngineer.AccessToken)))
            .ShouldBe(new MessagesBadgeResponse(0, false));
        var members = await OkAsync<List<ContactResponse>>(Client.GetAsync($"/api/messages/groups/{group.Id}/members", designer.AccessToken));
        members.Count.ShouldBe(4);

        // only people in it may mention, and be mentioned
        await (await Client.PostJsonAsync($"/api/messages/groups/{group.Id}", new { body = "Hi", mentionIds = new[] { outsider.User.Id } }, designer.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // anyone not in it, an admin of a business in it included, reads it as not there
        await (await Client.GetAsync($"/api/messages/groups/{group.Id}", outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/messages/groups/{group.Id}", new { body = "Hi" }, outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/messages/groups/{group.Id}/members", outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await InboxEntryAsync(group.Id, outsider)).ShouldBeNull();

        // the people in it hear of it live; nobody else does
        api.Live.SentTo(siteEngineer.User.Id, LiveSignals.Messages).ShouldContain(new LiveChat(null, null, GroupId: group.Id));
        api.Live.SentTo(outsider.User.Id, LiveSignals.Messages).ShouldNotContain(new LiveChat(null, null, GroupId: group.Id));
    }

    [Fact]
    public async Task Only_holders_of_manage_groups_make_and_change_groups_and_only_with_active_staff()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var admin = await PersonAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var designer = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var group = await CreateAsync(superAdmin, "Leads", admin.User.Id);

        await (await Client.PostJsonAsync("/api/messages/groups", new { name = "Mine", memberIds = Array.Empty<long>() }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync($"/api/messages/groups/{group.Id}", new { name = "Ours" }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/messages/groups/{group.Id}/members", new { userIds = new[] { designer.User.Id } }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/messages/groups/{group.Id}", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/messages/groups", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await (await Client.PostJsonAsync("/api/messages/groups", new { name = "  ", memberIds = Array.Empty<long>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/messages/groups", new { name = new string('x', 81), memberIds = Array.Empty<long>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/messages/groups", new { name = "Ghosts", memberIds = new[] { 999_999_999L } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync("/api/messages/groups/999999999", new { name = "Nowhere" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // any super admin manages every group, also one they aren't in, but reads only the groups they are in
        var (_, _, otherSuperAdmin) = await _steps.ActiveSuperAdminAsync();
        var listed = (await OkAsync<List<ChatGroupDetailResponse>>(Client.GetAsync("/api/messages/groups", otherSuperAdmin.AccessToken)))
            .Single(g => g.Id == group.Id);
        listed.IsMember.ShouldBeFalse();
        (await OkAsync<ChatGroupDetailResponse>(Client.PutJsonAsync($"/api/messages/groups/{group.Id}", new { name = "Design leads" }, otherSuperAdmin.AccessToken)))
            .Name.ShouldBe("Design leads");
        await (await Client.GetAsync($"/api/messages/groups/{group.Id}", otherSuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await PageAsync(group.Id, admin)).Items.Last().Body.ShouldBe("renamed the group to “Design leads”");
        (await OkAsync<List<ContactResponse>>(Client.GetAsync("/api/messages/groups/people", otherSuperAdmin.AccessToken)))
            .Select(p => p.Id).ShouldContain(designer.User.Id);
    }

    [Fact]
    public async Task People_added_see_the_history_none_of_it_unread_and_people_taken_out_or_leaving_lose_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var engineer = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var newcomer = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var leaver = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var group = await CreateAsync(superAdmin, "Handover", engineer.User.Id, leaver.User.Id);
        await OkAsync<MessageResponse>(Client.PostJsonAsync($"/api/messages/groups/{group.Id}", new { body = "Keys are with the guard" }, engineer.AccessToken));

        var added = await OkAsync<ChatGroupDetailResponse>(Client.PostJsonAsync(
            $"/api/messages/groups/{group.Id}/members", new { userIds = new[] { newcomer.User.Id, engineer.User.Id } }, superAdmin.AccessToken));
        added.Members.Count.ShouldBe(4);
        var history = await PageAsync(group.Id, newcomer);
        history.Items.Select(m => m.Body).ShouldContain("Keys are with the guard");
        history.Items.Last().Kind.ShouldBe(MessageKinds.GroupEvent);
        history.Items.Last().Body!.ShouldStartWith("added ");
        (await InboxEntryAsync(group.Id, newcomer))!.Unread.ShouldBe(0);

        // the person taken out loses the chat, history included, and hears of it live
        await OkAsync<ChatGroupDetailResponse>(Client.DeleteAsync($"/api/messages/groups/{group.Id}/members/{engineer.User.Id}", superAdmin.AccessToken));
        await (await Client.GetAsync($"/api/messages/groups/{group.Id}", engineer.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await InboxEntryAsync(group.Id, engineer)).ShouldBeNull();
        api.Live.SentTo(engineer.User.Id, LiveSignals.Messages).ShouldContain(new LiveChat(null, null, GroupId: group.Id));
        await (await Client.DeleteAsync($"/api/messages/groups/{group.Id}/members/{engineer.User.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // anyone in it may leave; anyone else reads it as not there
        (await Client.PostEmptyAsync($"/api/messages/groups/{group.Id}/leave", leaver.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await InboxEntryAsync(group.Id, leaver)).ShouldBeNull();
        await (await Client.PostEmptyAsync($"/api/messages/groups/{group.Id}/leave", leaver.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await PageAsync(group.Id, newcomer)).Items.Last().Body.ShouldBe("left the group");
    }

    [Fact]
    public async Task A_groups_photo_is_seen_by_its_people_and_deleting_the_group_takes_everything_in_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var member = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var outsider = await PersonAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var group = await CreateAsync(superAdmin, "Photos", member.User.Id);

        var photo = await FileTests.UploadPngAsync(Client, superAdmin.AccessToken, "group.png");
        (await OkAsync<ChatGroupDetailResponse>(Client.PutJsonAsync($"/api/messages/groups/{group.Id}/photo", new { fileId = photo.Id }, superAdmin.AccessToken)))
            .PhotoFileId.ShouldBe(photo.Id);
        (await InboxEntryAsync(group.Id, member))!.Group!.PhotoFileId.ShouldBe(photo.Id);
        (await Client.GetAsync($"/api/files/{photo.Id}", member.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.GetAsync($"/api/files/{photo.Id}", outsider.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var shared = await FileTests.UploadPngAsync(Client, member.AccessToken, "site.png");
        await OkAsync<MessageResponse>(Client.PostJsonAsync(
            $"/api/messages/groups/{group.Id}", new { body = "", attachmentIds = new[] { shared.Id } }, member.AccessToken));
        (await Client.GetAsync($"/api/files/{shared.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await Client.DeleteAsync($"/api/messages/groups/{group.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await InboxEntryAsync(group.Id, member)).ShouldBeNull();
        await (await Client.GetAsync($"/api/messages/groups/{group.Id}", member.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // the files go with it, the uploader's own included
        await (await Client.GetAsync($"/api/files/{shared.Id}", member.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/files/{photo.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        api.Live.SentTo(member.User.Id, LiveSignals.Messages).ShouldContain(new LiveChat(null, null, GroupId: group.Id));
    }
}
