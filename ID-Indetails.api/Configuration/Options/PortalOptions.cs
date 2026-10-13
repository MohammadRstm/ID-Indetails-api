using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>The client portal's sign-in: its token audience, its refresh cookie, invitations and the sign-in lockout.</summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>The audience of portal access tokens. Different from the staff one, so neither scheme accepts the other's tokens.</summary>
    [Required]
    public string Audience { get; init; } = "id-indetails-portal";

    /// <summary>How long an emailed invitation link stays valid.</summary>
    [Range(1, 24 * 14)]
    public int InvitationExpirationHours { get; init; } = 24;

    [Range(5, 24 * 60)]
    public int PasswordResetExpirationMinutes { get; init; } = 30;

    /// <summary>Lifetime of each portal refresh token. Rotation issues a new one on every refresh.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 1;

    [Required]
    public string CookieName { get; init; } = "idi_portal_refresh";

    /// <summary>Only the portal's auth endpoints receive the cookie; the staff cookie lives on <c>/api/auth</c>.</summary>
    [Required]
    public string CookiePath { get; init; } = "/api/portal/auth";

    /// <summary>Wrong passwords in a row before sign-in is refused for <see cref="LockoutMinutes"/>.</summary>
    [Range(3, 100)]
    public int LockoutAttempts { get; init; } = 10;

    [Range(1, 24 * 60)]
    public int LockoutMinutes { get; init; } = 15;
}
