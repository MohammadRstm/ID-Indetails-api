using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Hours a person is excused from in a month's hours ledger (sick leave, annual leave,
/// a public holiday, …): some minutes, specific days, or the whole month. Excused time is
/// paid unless <see cref="IsPaid"/> is false.
/// </summary>
public class HourExcuse
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>First day of the month the excuse belongs to.</summary>
    public DateOnly MonthStart { get; set; }

    public ExcuseKind Kind { get; set; }

    /// <summary>Only for <see cref="ExcuseKind.Minutes"/>.</summary>
    public int? Minutes { get; set; }

    public bool IsPaid { get; set; } = true;

    public string Note { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public long? DeletedById { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Only for <see cref="ExcuseKind.Days"/>.</summary>
    public ICollection<HourExcuseDay> Days { get; set; } = new List<HourExcuseDay>();
}
