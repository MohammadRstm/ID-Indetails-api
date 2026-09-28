using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Access-token settings. <see cref="SigningKey"/> is a secret: user secrets or environment only.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key. At least 32 characters (256 bits).</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 24 * 60)]
    public int AccessTokenMinutes { get; init; } = 180;
}
