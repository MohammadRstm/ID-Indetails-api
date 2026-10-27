namespace ID_Indetails.api.Models;

/// <summary>
/// An Execution employee overseeing a site: they report its ground labour and write its daily logs. Taken off the
/// site, the row stays with <see cref="RemovedAt"/> set, so their past records still say who they were.
/// </summary>
public class ProjectStaff
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long UserId { get; set; }

    public DateTime AssignedAt { get; set; }

    public long? AssignedById { get; set; }

    /// <summary>Null while they oversee the site.</summary>
    public DateTime? RemovedAt { get; set; }

    public Project Project { get; set; } = null!;

    public User User { get; set; } = null!;
}
