using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>SMTP settings. Locally this points at Mailpit; credentials, if any, belong in user secrets.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Host { get; init; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; init; } = 1025;

    /// <summary>Upgrade to TLS with STARTTLS. Off for Mailpit, on for real SMTP servers.</summary>
    public bool UseStartTls { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    [Required, EmailAddress]
    public string FromAddress { get; init; } = "no-reply@id-indetails.local";

    [Required]
    public string FromName { get; init; } = "Hourline";

    /// <summary>Product name used in email subjects and bodies.</summary>
    [Required]
    public string ProductName { get; init; } = "Hourline";
}
