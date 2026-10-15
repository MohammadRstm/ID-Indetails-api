namespace ID_Indetails.api.Models;

/// <summary>A one-time link for a client to choose a new portal password. Only the token's hash is stored.</summary>
public class PortalPasswordResetToken
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public PortalAccount Account { get; set; } = null!;
}
