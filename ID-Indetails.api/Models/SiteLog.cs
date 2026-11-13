namespace ID_Indetails.api.Models;

/// <summary>
/// One thing a site's staff noted on a day ("the concrete arrived with 3 bags missing"). A day can have many, each its
/// own; the writer can change one only on the day it was written. Never deleted: losses are analysed from them later.
/// </summary>
public class SiteLog
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long UserId { get; set; }

    /// <summary>What happened, in words. Empty when the log is a voice note only.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The voice note recorded with the log, from the writer's own upload; null when there is none.</summary>
    public long? VoiceFileId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;

    public User User { get; set; } = null!;
}
