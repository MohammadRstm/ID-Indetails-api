using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>How people reach the developer ("Contact the developer").</summary>
public sealed class BugReportOptions
{
    public const string SectionName = "BugReports";

    /// <summary>Where messages to the developer are emailed. Never sent to the client.</summary>
    [Required, EmailAddress]
    public string ToAddress { get; init; } = string.Empty;

    /// <summary>The developer's WhatsApp number, digits only with the country code (wa.me/{number}). Public: the sign-in pages show it.</summary>
    [Required, RegularExpression(@"^\d{8,15}$")]
    public string WhatsAppNumber { get; init; } = string.Empty;
}
