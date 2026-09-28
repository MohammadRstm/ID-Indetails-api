using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Per-IP limits on endpoints that accept secrets (passwords, tokens, the setup key).</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Login, refresh, invitation validate/accept: requests per IP per minute.</summary>
    [Range(1, 10_000)]
    public int AuthPermitsPerMinute { get; init; } = 10;

    /// <summary>Super-admin setup: requests per IP per hour.</summary>
    [Range(1, 10_000)]
    public int SetupPermitsPerHour { get; init; } = 5;
}
