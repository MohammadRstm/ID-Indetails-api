namespace ID_Indetails.api.Models;

/// <summary>
/// A person's own quiet hours, in minutes after midnight in the company's time zone. No row means the defaults
/// (22:00 to 07:00); both null means no quiet hours. The range may cross midnight.
/// </summary>
public class UserNotificationSettings
{
    public long UserId { get; set; }

    public short? QuietFromMin { get; set; }

    public short? QuietToMin { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
