using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Who gets which live signal. The signals are captured instead of sent (see <see cref="CapturingLivePublisher"/>).</summary>
public class LiveUpdateTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<AuthResponse> InTeamAsync(AuthResponse superAdmin, long teamId)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.PutInTeamAsync(person.User.Id, teamId);
        return person;
    }

    [Fact]
    public async Task A_message_signals_everyone_in_the_chat_and_a_failed_request_signals_no_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var engineering = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Engineering")).Id;
        var data = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Data")).Id;
        var engineer = await InTeamAsync(superAdmin, engineering);
        var teammate = await InTeamAsync(superAdmin, engineering);
        var analyst = await InTeamAsync(superAdmin, data);

        (await Client.PostJsonAsync($"/api/messages/teams/{engineering}", new { body = "Standup in 5" }, engineer.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var chat = new LiveChat(engineering, null);
        api.Live.SentTo(teammate.User.Id, LiveSignals.Messages).ShouldBe([chat]);
        api.Live.SentTo(engineer.User.Id, LiveSignals.Messages).ShouldBe([chat]);
        api.Live.SentTo(superAdmin.User.Id, LiveSignals.Messages).ShouldContain(chat);
        api.Live.SentTo(analyst.User.Id, LiveSignals.Messages).ShouldBeEmpty();

        // Not in that chat: refused, and nobody hears anything.
        await (await Client.PostJsonAsync($"/api/messages/teams/{engineering}", new { body = "Me too" }, analyst.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        api.Live.SentTo(teammate.User.Id, LiveSignals.Messages).Count.ShouldBe(1);

        // A direct message tells each side which chat changed; reading it tells the reader's other devices.
        (await Client.PostJsonAsync($"/api/messages/direct/{analyst.User.Id}", new { body = "Hi" }, engineer.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        api.Live.SentTo(analyst.User.Id, LiveSignals.Messages).ShouldBe([new LiveChat(null, engineer.User.Id)]);
        api.Live.SentTo(engineer.User.Id, LiveSignals.Messages).ShouldContain(new LiveChat(null, analyst.User.Id));
        (await Client.PostEmptyAsync($"/api/messages/direct/{engineer.User.Id}/read", analyst.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        api.Live.SentTo(analyst.User.Id, LiveSignals.Messages).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_new_notification_rings_the_bell_and_reading_it_updates_the_readers_other_devices()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var engineering = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Engineering")).Id;
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var employee = await InTeamAsync(superAdmin, engineering);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var rings = api.Live.SentTo(employee.User.Id, LiveSignals.Notifications).Count;
        rings.ShouldBeGreaterThan(0);

        var assigned = await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements",
            new { text = "Check the stair", assigneeIds = new[] { employee.User.Id } }, admin.AccessToken);
        ((int)assigned.StatusCode).ShouldBe(201, await assigned.Content.ReadAsStringAsync());
        api.Live.SentTo(employee.User.Id, LiveSignals.Notifications).Count.ShouldBe(rings + 1);

        var list = await (await Client.GetAsync("/api/notifications", employee.AccessToken)).ReadAsync<PagedResponse<NotificationResponse>>();
        (await Client.PostEmptyAsync($"/api/notifications/{list.Items[0].Id}/read", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        api.Live.SentTo(employee.User.Id, LiveSignals.Notifications).Count.ShouldBe(rings + 2);
    }

    [Fact]
    public async Task The_live_connection_needs_a_signed_in_user_by_header_or_query_token()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        const string negotiate = "/hubs/live/negotiate?negotiateVersion=1";

        (await Client.PostEmptyAsync(negotiate)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostEmptyAsync(negotiate, superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.PostEmptyAsync($"{negotiate}&access_token={Uri.EscapeDataString(superAdmin.AccessToken)}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The query token only counts on the live connection.
        (await Client.GetAsync($"/api/notifications/unread-count?access_token={Uri.EscapeDataString(superAdmin.AccessToken)}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
