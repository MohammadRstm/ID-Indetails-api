namespace ID_Indetails.api.Models;

/// <summary>
/// A portal account's access to one Design project. One row per account and project: ending access sets
/// <see cref="RevokedAt"/>, and giving it again clears it. The history is in the activity feed.
/// </summary>
public class PortalProjectAccess
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public long ProjectId { get; set; }

    public DateTime GrantedAt { get; set; }

    public long GrantedById { get; set; }

    /// <summary>Null while the client has access.</summary>
    public DateTime? RevokedAt { get; set; }

    public long? RevokedById { get; set; }

    /// <summary>When the client last opened the project's page; null until they do. The project list's news flag compares with it.</summary>
    public DateTime? LastOpenedAt { get; set; }

    public PortalAccount Account { get; set; } = null!;

    public Project Project { get; set; } = null!;
}
