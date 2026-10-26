using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A project of one domain. Only the current status is stored here; status and progress
/// history are recorded in <see cref="ActivityEvent"/>. Cost, profit and margin are never
/// stored: they are calculated from entries, expenses, materials and labour.
/// </summary>
public class Project
{
    public long Id { get; set; }

    public string DomainCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Where the work is: a site's address, a building, a town. Free text, optional.</summary>
    public string? Location { get; set; }

    public string? Icon { get; set; }

    public string? Tint { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public BillingType BillingType { get; set; } = BillingType.Client;

    /// <summary>Must be null for internal projects, and must belong to the same domain.</summary>
    public long? ClientId { get; set; }

    public long OwnerId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? TargetDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Agreed contract value (not cash received). Always 0 for internal projects.
    /// Design: typed in by an admin. Execution: 0 while open, because the agreed amount is
    /// calculated live (material client prices + project fee); finalising writes the frozen
    /// agreed amount here.
    /// </summary>
    public decimal Revenue { get; set; }

    public string RevenueCurrency { get; set; } = "USD";

    public DateTime? RevenueLockedAt { get; set; }

    public DateTime? FinalizedAt { get; set; }

    /// <summary>Execution only (required there, null for Design): fixed charge for doing the job.</summary>
    public decimal? ProjectFee { get; set; }

    /// <summary>
    /// Execution only (required there, null for Design): default markup on material cost,
    /// 0.10 = 10%, up to 5 (500%). Materials without their own client price use it.
    /// </summary>
    public decimal? MaterialMarkup { get; set; }

    /// <summary>
    /// Cached done / total requirements, 0–100. Updated in the same transaction as every
    /// requirement change, which also records a progress_set event.
    /// </summary>
    public byte ProgressPct { get; set; }

    /// <summary>Execution only (required there, null for Design): whether its client payments go into the box.</summary>
    public BoxState? BoxState { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Domain Domain { get; set; } = null!;

    public Client? Client { get; set; }

    public User Owner { get; set; } = null!;

    public ICollection<ProjectRequirement> Requirements { get; set; } = new List<ProjectRequirement>();

    public ICollection<ProjectMeeting> Meetings { get; set; } = new List<ProjectMeeting>();

    public ICollection<ProjectExpense> Expenses { get; set; } = new List<ProjectExpense>();

    public ICollection<ProjectLabourDefault> LabourDefaults { get; set; } = new List<ProjectLabourDefault>();

    public ICollection<ProjectPaymentChange> PaymentChanges { get; set; } = new List<ProjectPaymentChange>();

    /// <summary>Execution only: who oversees the site, now and before.</summary>
    public ICollection<ProjectStaff> Staff { get; set; } = new List<ProjectStaff>();
}
