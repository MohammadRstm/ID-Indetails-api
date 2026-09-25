namespace ID_Indetails.api.Models;

public class MessageAttachment
{
    public long MessageId { get; set; }

    public long FileId { get; set; }

    public Message Message { get; set; } = null!;

    public StoredFile File { get; set; } = null!;
}
