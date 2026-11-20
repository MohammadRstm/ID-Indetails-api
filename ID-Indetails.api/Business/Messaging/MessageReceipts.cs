using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Messaging;

/// <summary>
/// Who has received and read a message, from the read markers (decided 20 November 2026):
/// <list type="bullet">
/// <item>A message is for everyone in its chat but its sender; in Announcements, only for those who had joined by
/// then (what was announced before someone joined isn't theirs to catch up on).</item>
/// <item>Someone has seen it once their read marker reaches it, and received it once their received marker (or their
/// read marker) does.</item>
/// <item>Its ticks: two blue once everyone it is for has seen it, two grey once all of them have received it, one
/// grey before. A message for nobody (a team chat of one) stays at one grey.</item>
/// <item>When they saw it: the exact time for an announcement; in the other chats only when it is the last message
/// they read, since the read marker keeps only when it last moved.</item>
/// <item>Who sees all this: a message's sender; in team chats the super admins too, for any message; in
/// Announcements whoever may announce.</item>
/// </list>
/// </summary>
public static class MessageReceipts
{
    public enum State
    {
        NotReceived,
        Delivered,
        Seen,
    }

    /// <param name="ReadMovedAt">When the read marker last moved.</param>
    public sealed record Marker(long? Read, long? Delivered, DateTime ReadMovedAt);

    public static bool IsFor(long personId, DateTime joinedAt, long senderId, DateTime sentAt, ConversationType type)
        => personId != senderId && (type != ConversationType.Announcements || joinedAt <= sentAt);

    public static State StateOf(long messageId, Marker? marker)
    {
        if (marker?.Read >= messageId)
        {
            return State.Seen;
        }

        return Math.Max(marker?.Read ?? 0, marker?.Delivered ?? 0) >= messageId ? State.Delivered : State.NotReceived;
    }

    public static ReceiptStatus TicksOf(IReadOnlyCollection<State> states)
    {
        if (states.Count == 0)
        {
            return ReceiptStatus.Sent;
        }

        if (states.All(s => s == State.Seen))
        {
            return ReceiptStatus.Read;
        }

        return states.All(s => s != State.NotReceived) ? ReceiptStatus.Delivered : ReceiptStatus.Sent;
    }

    public static DateTime? SeenAt(long messageId, Marker marker, DateTime? announcementReadAt)
        => announcementReadAt ?? (marker.Read == messageId ? marker.ReadMovedAt : null);

    public static bool CanSee(ConversationType type, long viewerId, bool viewerIsSuperAdmin, bool viewerAnnounces, long senderId)
        => viewerId == senderId || type switch
        {
            ConversationType.Group => viewerIsSuperAdmin,
            ConversationType.Announcements => viewerAnnounces,
            _ => false,
        };
}
