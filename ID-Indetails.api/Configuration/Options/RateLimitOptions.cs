using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// Request limits. Every request counts against a global limit (per signed-in user, otherwise per IP);
/// endpoints that accept secrets also have a stricter per-IP policy.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Every request of a signed-in user, per user per minute.</summary>
    [Range(1, 100_000)]
    public int UserPermitsPerMinute { get; init; } = 300;

    /// <summary>Every request without a valid access token, per IP per minute.</summary>
    [Range(1, 100_000)]
    public int AnonymousPermitsPerMinute { get; init; } = 60;

    /// <summary>CSV exports, per signed-in user per minute.</summary>
    [Range(1, 10_000)]
    public int ExportPermitsPerMinute { get; init; } = 10;

    /// <summary>File uploads, per signed-in user per minute.</summary>
    [Range(1, 10_000)]
    public int UploadPermitsPerMinute { get; init; } = 30;

    /// <summary>Session refreshes, per IP per minute. Every page load refreshes, so a shared office address needs room.</summary>
    [Range(1, 10_000)]
    public int RefreshPermitsPerMinute { get; init; } = 60;

    /// <summary>Requests that send email to an address the caller chooses, per signed-in user per minute.</summary>
    [Range(1, 10_000)]
    public int EmailPermitsPerMinute { get; init; } = 10;

    /// <summary>
    /// Login, password reset, invitation validate/accept: requests per IP per minute. Also the password checks of a
    /// signed-in person (change password or sign-in address), per user.
    /// </summary>
    [Range(1, 10_000)]
    public int AuthPermitsPerMinute { get; init; } = 10;

    /// <summary>Bug reports, per signed-in user per hour.</summary>
    [Range(1, 1_000)]
    public int BugReportPermitsPerHour { get; init; } = 5;

    /// <summary>A client's chat messages and comments, per portal account per minute.</summary>
    [Range(1, 10_000)]
    public int PortalWritePermitsPerMinute { get; init; } = 30;

    /// <summary>Questions to Proton, per signed-in user per minute. Each one costs model calls.</summary>
    [Range(1, 10_000)]
    public int ProtonPermitsPerMinute { get; init; } = 10;

    /// <summary>Super-admin setup: requests per IP per hour.</summary>
    [Range(1, 10_000)]
    public int SetupPermitsPerHour { get; init; } = 5;
}
