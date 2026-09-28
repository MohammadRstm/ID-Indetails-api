using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Refresh-token lifetime and the HttpOnly cookie that carries it.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    /// <summary>Lifetime of each refresh token. Rotation issues a new one on every refresh.</summary>
    [Range(1, 90)]
    public int ExpirationDays { get; init; } = 1;

    [Required]
    public string CookieName { get; init; } = "idi_refresh";

    /// <summary>The cookie is only sent to the auth endpoints, never to the rest of the API.</summary>
    [Required]
    public string CookiePath { get; init; } = "/api/auth";

    /// <summary>Strict, Lax or None. None is only needed if the client runs on another site.</summary>
    [Required]
    public string SameSite { get; init; } = "Strict";
}
