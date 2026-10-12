namespace ID_Indetails.api.Models;

/// <summary>
/// A chat with Proton, kept so its owner can read it again and carry on: only its owner ever sees it. The access the
/// owner had when it was held is kept with it, so a chat can't show what they may no longer see.
/// </summary>
public class ProtonStoredConversation
{
    /// <summary>The conversation's id, the same one the chat used while it was held in memory.</summary>
    public Guid Id { get; set; }

    public long UserId { get; set; }

    /// <summary>The first question, shortened, until the owner renames it.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>When its last message was saved.</summary>
    public DateTime UpdatedAt { get; set; }

    public int MessageCount { get; set; }

    /// <summary>A hash of the owner's role, permissions and domains when the chat began (ProtonHistoryRules.AccessKey).</summary>
    public string AccessFingerprint { get; set; } = string.Empty;

    /// <summary>JSON list of the permission codes the owner had when the chat began.</summary>
    public string AccessPermissions { get; set; } = "[]";

    /// <summary>JSON list of the domains the owner could see when the chat began.</summary>
    public string AccessDomains { get; set; } = "[]";

    public User User { get; set; } = null!;

    public List<ProtonStoredMessage> Messages { get; set; } = [];
}
