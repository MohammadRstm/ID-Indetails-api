using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One message of a saved chat, as its owner saw it: their question, or Proton's answer with what was shown with it
/// (links, what an analysis was based on, cards, charts and tables). Never the tool results behind it.
/// </summary>
public class ProtonStoredMessage
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Its place in the chat, from 1.</summary>
    public int Seq { get; set; }

    public ProtonMessageRole Role { get; set; }

    /// <summary>How the user asked: typed, a picked option, a suggested reply or spoken. Null on Proton's messages.</summary>
    public ProtonMessageOrigin? Origin { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>JSON: the records the answer named, for links.</summary>
    public string? Refs { get; set; }

    /// <summary>JSON: what the answer's figures rest on ("Based on").</summary>
    public string? Sources { get; set; }

    /// <summary>JSON: the ids of the changes proposed with the answer; their outcome is in proton_actions.</summary>
    public string? ActionIds { get; set; }

    /// <summary>JSON: the charts and tables shown with the answer, exactly as they were shown.</summary>
    public string? Blocks { get; set; }

    public DateTime CreatedAt { get; set; }

    public ProtonStoredConversation Conversation { get; set; } = null!;
}
