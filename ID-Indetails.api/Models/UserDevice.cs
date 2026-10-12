using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A phone that receives pushes, tied to the session it was registered from: it is only sent to while that session
/// still has a valid refresh token. A push token belongs to one person: registering it again moves it.
/// </summary>
public class UserDevice
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>The phone's session (<c>refresh_tokens.session_id</c>).</summary>
    public Guid SessionId { get; set; }

    public PushProvider Provider { get; set; }

    public string PushToken { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public string? AppVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
