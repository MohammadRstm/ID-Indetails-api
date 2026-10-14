namespace ID_Indetails.api.Enums;

/// <summary>What a client's notification is about.</summary>
public enum PortalNotificationKind
{
    /// <summary>The team wrote in the project chat (rolling: one per project until read).</summary>
    TeamMessage,

    /// <summary>The team commented on a file (rolling: one per file until read).</summary>
    TeamComment,

    /// <summary>Someone mentioned the client, in the chat or a comment.</summary>
    Mention,
}
