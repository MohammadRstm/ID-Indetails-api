namespace ID_Indetails.api.Models;

/// <summary>
/// A one-time signup link sent to an invited user. Resending revokes the previous invitation
/// and creates a new one.
/// </summary>
public class UserInvitation
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>Hash of the token in the emailed link. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime SentAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public long? InvitedById { get; set; }

    public User User { get; set; } = null!;
}
