namespace ID_Indetails.api.Models;

/// <summary>
/// A closed month a super admin opened again for logging, until <see cref="OpenUntil"/> (that day included): people log,
/// change and remove their own work in it as in the current month. It locks again by itself after that day.
/// </summary>
public class ReopenedMonth
{
    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    /// <summary>The last day work in it can still change.</summary>
    public DateOnly OpenUntil { get; set; }

    public string Note { get; set; } = string.Empty;

    public long ReopenedById { get; set; }

    public DateTime ReopenedAt { get; set; }

    public User ReopenedBy { get; set; } = null!;
}
