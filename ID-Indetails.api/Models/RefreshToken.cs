using ID_Indetails.api.Enums;

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

    public RefreshTokenClient Client { get; set; }

    /// <summary>The chain of tokens one sign-in grows into. Null on rows from before sessions were recorded.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Null while valid, and on rows from before reasons were recorded.</summary>
    public RevokeReason? RevokeReason { get; set; }

    public User User { get; set; } = null!;
}
