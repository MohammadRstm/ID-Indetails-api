using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.Enums;
using Shouldly;
using M = ID_Indetails.api.Business.Messaging.MessageReceipts.Marker;
using S = ID_Indetails.api.Business.Messaging.MessageReceipts.State;

namespace ID_Indetails.UnitTests.Business;

public class MessageReceiptsTests
{
    private static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Someone_has_seen_a_message_once_their_read_marker_reaches_it_and_received_it_once_either_marker_does()
    {
        MessageReceipts.StateOf(10, null).ShouldBe(S.NotReceived);
        MessageReceipts.StateOf(10, new M(9, null, Noon)).ShouldBe(S.NotReceived);
        MessageReceipts.StateOf(10, new M(null, 10, Noon)).ShouldBe(S.Delivered);
        MessageReceipts.StateOf(10, new M(9, 12, Noon)).ShouldBe(S.Delivered);
        MessageReceipts.StateOf(10, new M(10, null, Noon)).ShouldBe(S.Seen);
        MessageReceipts.StateOf(10, new M(15, 9, Noon)).ShouldBe(S.Seen);
    }

    [Fact]
    public void The_ticks_turn_blue_only_once_everyone_has_seen_it_and_double_once_everyone_has_received_it()
    {
        MessageReceipts.TicksOf([]).ShouldBe(ReceiptStatus.Sent);
        MessageReceipts.TicksOf([S.NotReceived, S.Seen]).ShouldBe(ReceiptStatus.Sent);
        MessageReceipts.TicksOf([S.Delivered, S.Seen]).ShouldBe(ReceiptStatus.Delivered);
        MessageReceipts.TicksOf([S.Delivered]).ShouldBe(ReceiptStatus.Delivered);
        MessageReceipts.TicksOf([S.Seen, S.Seen]).ShouldBe(ReceiptStatus.Read);
    }

    [Fact]
    public void When_someone_saw_it_is_known_for_announcements_and_for_the_last_message_they_read()
    {
        var at = Noon.AddMinutes(-5);
        MessageReceipts.SeenAt(10, new M(10, 10, Noon), null).ShouldBe(Noon);
        MessageReceipts.SeenAt(10, new M(12, 12, Noon), null).ShouldBeNull();
        MessageReceipts.SeenAt(10, new M(12, 12, Noon), at).ShouldBe(at);
    }

    [Fact]
    public void A_message_is_for_everyone_but_its_sender_and_an_announcement_only_for_those_who_had_joined()
    {
        MessageReceipts.IsFor(2, Noon, 1, Noon, ConversationType.Group).ShouldBeTrue();
        MessageReceipts.IsFor(1, Noon, 1, Noon, ConversationType.Dm).ShouldBeFalse();
        MessageReceipts.IsFor(2, Noon.AddDays(1), 1, Noon, ConversationType.Group).ShouldBeTrue();
        MessageReceipts.IsFor(2, Noon.AddDays(1), 1, Noon, ConversationType.Announcements).ShouldBeFalse();
        MessageReceipts.IsFor(2, Noon.AddDays(-1), 1, Noon, ConversationType.Announcements).ShouldBeTrue();
    }

    [Fact]
    public void The_sender_sees_receipts_and_so_do_super_admins_in_team_chats_and_announcers_in_announcements()
    {
        MessageReceipts.CanSee(ConversationType.Dm, 1, false, false, 1).ShouldBeTrue();
        MessageReceipts.CanSee(ConversationType.Dm, 2, true, true, 1).ShouldBeFalse();
        MessageReceipts.CanSee(ConversationType.Group, 2, false, false, 1).ShouldBeFalse();
        MessageReceipts.CanSee(ConversationType.Group, 2, true, false, 1).ShouldBeTrue();
        MessageReceipts.CanSee(ConversationType.Announcements, 2, true, false, 1).ShouldBeFalse();
        MessageReceipts.CanSee(ConversationType.Announcements, 2, false, true, 1).ShouldBeTrue();
    }
}
