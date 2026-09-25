namespace ID_Indetails.api.Models;

/// <summary>A user's saved card order for one dashboard.</summary>
public class UserBoardLayout
{
    public long UserId { get; set; }

    public string BoardKey { get; set; } = string.Empty;

    /// <summary>JSON document with the card order.</summary>
    public string Layout { get; set; } = "{}";

    public DateTime SavedAt { get; set; }

    public User User { get; set; } = null!;
}
