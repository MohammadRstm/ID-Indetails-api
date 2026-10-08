namespace ID_Indetails.api.Models;

public class Message
{
    public long Id { get; set; }

    public long ConversationId { get; set; }

    public long SenderId { get; set; }

    /// <summary>May be empty when the message only carries attachments.</summary>
    public string? Body { get; set; }

    /// <summary>"text", or "task_comment" for a work-entry comment shared to a group.</summary>
    public string Kind { get; set; } = "text";

    public long? RefEntryId { get; set; }

    public long? RefCommentId { get; set; }

    /// <summary>The confirmed Proton action that sent it as the sender, so the chat can say so. Null for everything typed.</summary>
    public long? ProtonActionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public User Sender { get; set; } = null!;

    public ICollection<MessageMention> Mentions { get; set; } = new List<MessageMention>();

    public ICollection<MessageAttachment> Attachments { get; set; } = new List<MessageAttachment>();
}
