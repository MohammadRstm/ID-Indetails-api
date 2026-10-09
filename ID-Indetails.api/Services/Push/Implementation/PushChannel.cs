using System.Threading.Channels;
using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// The in-memory queue between the requests that cause pushes and the sender (<c>Jobs/PushDispatcher</c>). It lives in
/// this process: a push still queued when the API stops is lost, but its notification row and the in-app list are not.
/// Bounded, so a stalled sender can't grow it without limit: the oldest waiting pushes are dropped first.
/// </summary>
public sealed class PushChannel
{
    public const int Capacity = 10_000;

    private readonly Channel<PushJob> _channel = Channel.CreateBounded<PushJob>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<PushJob> Reader => _channel.Reader;

    public void Write(PushJob job) => _channel.Writer.TryWrite(job);
}
