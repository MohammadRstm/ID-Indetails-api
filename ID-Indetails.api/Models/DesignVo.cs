using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A variation order on a Design client project: lines that add to the price or take from it (before TVA). Applied,
/// its total joins the project's price and its amounts the departments' budgets; pending, it waits for the client.
/// </summary>
public class DesignVo
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary><c>VO-YYYY-NNN</c>, a series of Design's own.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DesignVoStatus Status { get; set; }

    public DesignVoSplit Split { get; set; }

    /// <summary>The lines' total before TVA, in USD; below zero for less work.</summary>
    public decimal TotalUsd { get; set; }

    /// <summary>The project's TVA when the VO was made.</summary>
    public decimal TvaRate { get; set; }

    /// <summary>Hours logged outside the agreed scope since <see cref="OutsideScopeFrom"/>, fixed when it was made; null when not shown.</summary>
    public int? OutsideScopeMinutes { get; set; }

    public DateOnly? OutsideScopeFrom { get; set; }

    public DateTime CreatedAt { get; set; }

    public long CreatedById { get; set; }

    /// <summary>When it was sent for approval (made pending).</summary>
    public DateTime? SentAt { get; set; }

    public long? SentById { get; set; }

    public DateTime? AppliedAt { get; set; }

    public long? AppliedById { get; set; }

    public DateTime? CancelledAt { get; set; }

    public long? CancelledById { get; set; }

    public Project Project { get; set; } = null!;

    public ICollection<DesignVoLine> Lines { get; set; } = new List<DesignVoLine>();

    /// <summary>Each department's part of the total, once applied (or chosen by hand when it was made).</summary>
    public ICollection<DesignVoAllocation> Allocations { get; set; } = new List<DesignVoAllocation>();
}

public class DesignVoLine
{
    public long Id { get; set; }

    public long DesignVoId { get; set; }

    public int Position { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Before TVA, in USD; below zero for work taken out.</summary>
    public decimal Amount { get; set; }
}

public class DesignVoAllocation
{
    public long DesignVoId { get; set; }

    public long TeamId { get; set; }

    public decimal Amount { get; set; }

    public Team Team { get; set; } = null!;
}
