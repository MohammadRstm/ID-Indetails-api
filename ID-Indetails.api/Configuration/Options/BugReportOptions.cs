using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Where bug reports go. Never sent to the client.</summary>
public sealed class BugReportOptions
{
    public const string SectionName = "BugReports";

    [Required, EmailAddress]
    public string ToAddress { get; init; } = string.Empty;
}
