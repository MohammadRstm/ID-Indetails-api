using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// Company defaults used until the company_settings row is seeded (it is empty for now).
/// </summary>
public sealed class CompanyOptions
{
    public const string SectionName = "Company";

    /// <summary>IANA time zone that decides what "today" means (join dates, pay effective dates).</summary>
    [Required]
    public string TimeZone { get; init; } = "Asia/Beirut";
}
