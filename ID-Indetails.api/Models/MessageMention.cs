namespace ID_Indetails.api.Models;

public class MessageMention
{
    public long MessageId { get; set; }

    public long UserId { get; set; }

    public Message Message { get; set; } = null!;

    public User User { get; set; } = null!;
}
