namespace ID_Indetails.api.Models;

/// <summary>
/// Execution only: the company taking some of a site's supervision fee out of the project account. It is not a cost,
/// so it never changes the site's profit; it only lowers the cash left in the account and what supervision is still
/// to be taken. Removed with <see cref="DeletedAt"/>, never deleted.
/// </summary>
public class SupervisionWithdrawal
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>USD, more than zero.</summary>
    public decimal AmountUsd { get; set; }

    /// <summary>The day it was taken, in the company time zone; not in the future.</summary>
    public DateOnly TakenOn { get; set; }

    public string? Note { get; set; }

    public long CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project Project { get; set; } = null!;
}
