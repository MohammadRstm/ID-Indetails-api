namespace ID_Indetails.api.Models;

/// <summary>Money a super admin added to Proton's budget for one month, on top of the configured monthly budget.</summary>
public class ProtonBudgetTopUp
{
    public long Id { get; set; }

    /// <summary>First day of the company-time-zone month the top-up is for.</summary>
    public DateOnly Month { get; set; }

    public decimal AmountUsd { get; set; }

    public string? Note { get; set; }

    public long AddedById { get; set; }

    public DateTime AddedAt { get; set; }

    public User AddedBy { get; set; } = null!;
}
