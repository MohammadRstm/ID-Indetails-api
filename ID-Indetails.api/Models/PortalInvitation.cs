namespace ID_Indetails.api.Models;

/// <summary>
/// A one-time link inviting a client to one Design project. Accepting it sets the account's password and gives access to
/// the project; resending revokes the earlier link. Only the token's hash is stored.
/// </summary>
public class PortalInvitation
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public long ProjectId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public long InvitedById { get; set; }

    public PortalAccount Account { get; set; } = null!;

    public Project Project { get; set; } = null!;
}
