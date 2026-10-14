using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A client's in-app notification. Rolling ones share a digest key while unread (more news adds to the count);
/// mentions have a key of their own, so a repeated request never notifies twice. Shown only while the client still
/// has access to the project.
/// </summary>
public class PortalNotification
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public long ProjectId { get; set; }

    public PortalNotificationKind Kind { get; set; }

    /// <summary>The file, for comments; not a foreign key, the file may be unpublished later.</summary>
    public long? SubjectId { get; set; }

    /// <summary>Who wrote and on which file, as JSON; never the text written.</summary>
    public string? Payload { get; set; }

    public string DigestKey { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}
