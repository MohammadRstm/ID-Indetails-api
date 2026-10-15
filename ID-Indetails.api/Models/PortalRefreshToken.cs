using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A client portal sign-in, rotated on every refresh like the staff web one. Its cookie is scoped to the portal's
/// auth endpoints, so staff and client sessions never meet.
/// </summary>
public class PortalRefreshToken
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    /// <summary>Hash of the cookie value. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The chain of tokens one sign-in grows into.</summary>
    public Guid SessionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>The token issued when this one was rotated. Reuse of a replaced token signals theft.</summary>
    public long? ReplacedById { get; set; }

    public RevokeReason? RevokeReason { get; set; }
}
