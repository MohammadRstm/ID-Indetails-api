namespace ID_Indetails.api.Models;

public class PasswordResetToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>Hash of the token in the emailed link. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
