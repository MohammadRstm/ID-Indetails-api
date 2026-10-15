using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.Realtime.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class LiveUpdatesTests
{
    private sealed class Capturing : ILivePublisher, IPortalLivePublisher
    {
        public List<(IReadOnlyCollection<long> UserIds, string Signal, object? Payload)> Sent { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<long> userIds, string signal, object? payload, CancellationToken cancellationToken = default)
        {
            Sent.Add((userIds, signal, payload));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_request_sends_each_person_each_signal_once_then_nothing_more()
    {
        var publisher = new Capturing();
        var live = new LiveUpdates(publisher, new Capturing());
        var engineering = new LiveChat(1, null);

        live.NotificationsChanged([3, 4]);
        live.NotificationsChanged([4, 5]);
        live.ChatChanged(3, engineering);
        live.ChatChanged(4, engineering);
        live.ChatChanged(3, engineering);
        live.ChatChanged(4, new LiveChat(null, 3));
        await live.FlushAsync();

        publisher.Sent.Count.ShouldBe(3);
        publisher.Sent[0].ShouldSatisfyAllConditions(
            s => s.Signal.ShouldBe(LiveSignals.Notifications),
            s => s.UserIds.ShouldBe([3, 4, 5], ignoreOrder: true),
            s => s.Payload.ShouldBeNull());
        publisher.Sent.ShouldContain(s => s.Signal == LiveSignals.Messages && Equals(s.Payload, engineering) && s.UserIds.Count == 2);
        publisher.Sent.ShouldContain(s => Equals(s.Payload, new LiveChat(null, 3)) && s.UserIds.Single() == 4);

        await live.FlushAsync();
        publisher.Sent.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Discarded_signals_are_never_sent()
    {
        var publisher = new Capturing();
        var live = new LiveUpdates(publisher, new Capturing());

        live.NotificationsChanged([3]);
        live.Discard();
        await live.FlushAsync();

        publisher.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Portal_signals_go_to_staff_through_the_staff_hub_and_to_clients_through_theirs()
    {
        var staff = new Capturing();
        var clients = new Capturing();
        var live = new LiveUpdates(staff, clients);

        live.PortalChanged(7, [3, 4], [10]);
        live.PortalChanged(7, [3], [10, 11]);
        live.PortalNotificationsChanged([10]);
        await live.FlushAsync();

        staff.Sent.Single().ShouldSatisfyAllConditions(
            s => s.Signal.ShouldBe(LiveSignals.Portal),
            s => s.UserIds.ShouldBe([3, 4], ignoreOrder: true));
        clients.Sent.ShouldContain(s => s.Signal == LiveSignals.Portal && s.UserIds.Count == 2);
        clients.Sent.ShouldContain(s => s.Signal == LiveSignals.Notifications && s.UserIds.Single() == 10);
        clients.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Clients_who_lost_access_are_told_once_through_their_hub()
    {
        var staff = new Capturing();
        var clients = new Capturing();
        var live = new LiveUpdates(staff, clients);

        live.PortalAccessChanged([10]);
        live.PortalAccessChanged([10, 11]);
        await live.FlushAsync();

        staff.Sent.ShouldBeEmpty();
        clients.Sent.Single().ShouldSatisfyAllConditions(
            s => s.Signal.ShouldBe(LiveSignals.Access),
            s => s.UserIds.ShouldBe([10, 11], ignoreOrder: true));
    }
}
