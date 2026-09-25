namespace ID_Indetails.api.Models;

public class RefreshToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>Hash of the token. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>The token issued when this one was rotated. Reuse of a replaced token signals theft.</summary>
    public long? ReplacedById { get; set; }

    public User User { get; set; } = null!;
}
