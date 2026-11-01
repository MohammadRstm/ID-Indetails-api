namespace ID_Indetails.api.Models;

/// <summary>
/// A once-only notification went to this person (an overdue invoice, a low profit, a missed deadline): it isn't sent
/// to them again under the same key. Kept apart from the notification itself, which is deleted after a week.
/// </summary>
public class NotificationSentKey
{
    public long RecipientId { get; set; }

    public string Key { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }

    public User Recipient { get; set; } = null!;
}
