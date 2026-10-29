using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A change refused because it would take a project's profit to zero or below: an expense, a material, a labour week,
/// logged hours, or a lower payment. Kept with the request itself, so whoever sees the project's money can force it
/// through (once) or dismiss it.
/// </summary>
public class BlockedChange
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public BlockedChangeKind Kind { get; set; }

    /// <summary>The refused request, as JSON with its route ids: what forcing it replays.</summary>
    public string Payload { get; set; } = string.Empty;

    public long RequestedById { get; set; }

    public DateTime RequestedAt { get; set; }

    public BlockedChangeStatus Status { get; set; } = BlockedChangeStatus.Pending;

    public long? DecidedById { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>The id of what forcing it made (the expense, the material, the entry…), when it made one.</summary>
    public long? ResultId { get; set; }

    public Project Project { get; set; } = null!;
}
