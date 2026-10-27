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

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;

    public User User { get; set; } = null!;
}
