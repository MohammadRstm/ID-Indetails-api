using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.Realtime.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class LiveUpdatesTests
{
    private sealed class Capturing : ILivePublisher
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
        var live = new LiveUpdates(publisher);
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
        var live = new LiveUpdates(publisher);

        live.NotificationsChanged([3]);
        live.Discard();
        await live.FlushAsync();

        publisher.Sent.ShouldBeEmpty();
    }
}
