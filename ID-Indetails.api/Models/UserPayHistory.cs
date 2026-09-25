using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Dated pay terms. The row in force on a day (latest <see cref="EffectiveFrom"/> on or before it)
/// costs the work done that day. Changes apply forward only; past rows are never edited.
/// </summary>
public class UserPayHistory
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public PayBasis Basis { get; set; }

    /// <summary>Hourly rate or monthly salary, depending on <see cref="Basis"/>.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Internal hourly cost used for project labour. Derived for monthly pay
    /// (salary / 173.33) and frozen here so past project costs never change.
    /// </summary>
    public decimal HourlyCostRate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;
}
