namespace ID_Indetails.api.Models;

/// <summary>
/// Append-only history of everything that happened. The activity feed, the project
/// timeline's status and progress reconstruction, and finalize snapshots all read it.
/// Rows are never updated or deleted.
/// </summary>
public class ActivityEvent
{
    public long Id { get; set; }

    /// <summary>Null for company-level events.</summary>
    public string? DomainCode { get; set; }

    /// <summary>Event kind, e.g. project_created, progress_set, invoice_sent.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    /// <summary>Who did it. Null for system events and demo data.</summary>
    public long? ActorId { get; set; }

    public long? ProjectId { get; set; }

    public long? ClientId { get; set; }

    /// <summary>The other record involved, e.g. "invoice" and its id.</summary>
    public string? SubjectType { get; set; }

    public long? SubjectId { get; set; }

    /// <summary>New value for money and progress events.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Previous value for money and progress events.</summary>
    public decimal? PrevAmount { get; set; }

    /// <summary>Kind-specific details as JSON (reason, channel, markup, …).</summary>
    public string? Payload { get; set; }

    /// <summary>
    /// True for seeded demonstration records. Demo rows must never be presented as genuine
    /// recorded history.
    /// </summary>
    public bool IsDemo { get; set; }
}
