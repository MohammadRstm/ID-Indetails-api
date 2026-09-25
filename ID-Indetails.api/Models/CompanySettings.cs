namespace ID_Indetails.api.Models;

/// <summary>
/// Company-wide settings. This is a single-company system, so the table holds exactly one
/// row: a CHECK constraint pins <see cref="Id"/> to 1.
/// </summary>
public class CompanySettings
{
    public const byte SingletonId = 1;

    public byte Id { get; set; } = SingletonId;

    public string Name { get; set; } = string.Empty;

    public long? LogoFileId { get; set; }

    /// <summary>IANA zone used for overtime, day boundaries and "today" (e.g. Asia/Beirut).</summary>
    public string TimeZone { get; set; } = "Asia/Beirut";

    /// <summary>0 = Sunday, 1 = Monday.</summary>
    public byte WeekStart { get; set; } = 1;

    public bool Use24HourTime { get; set; }

    public string DefaultCurrency { get; set; } = "USD";

    public decimal OvertimeMultiplier { get; set; } = 1m;

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }
}
