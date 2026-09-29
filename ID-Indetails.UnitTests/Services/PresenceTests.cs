using ID_Indetails.api.Services.Realtime.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class PresenceTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Someone_stays_online_until_their_last_connection_closes()
    {
        var presence = new Presence();
        presence.Connected(7);
        presence.Connected(7);
        presence.Connected(8);

        presence.Disconnected(7).ShouldBeFalse();
        presence.IsOnline(7).ShouldBeTrue();
        presence.OnlineUserIds().ShouldBe([7L, 8L], ignoreOrder: true);

        presence.Disconnected(7).ShouldBeTrue();
        presence.IsOnline(7).ShouldBeFalse();
        presence.OnlineUserIds().ShouldBe([8L]);
        presence.IsOnline(9).ShouldBeFalse();
    }

    [Fact]
    public void Last_seen_is_recorded_at_most_every_five_minutes_each()
    {
        var presence = new Presence();

        presence.ShouldRecordSeen(7, Now).ShouldBeTrue();
        presence.ShouldRecordSeen(7, Now.AddMinutes(4)).ShouldBeFalse();
        presence.ShouldRecordSeen(8, Now.AddMinutes(4)).ShouldBeTrue();
        presence.ShouldRecordSeen(7, Now.AddMinutes(5)).ShouldBeTrue();
    }
}
