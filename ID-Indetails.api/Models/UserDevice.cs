using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A phone or browser that receives pushes, tied to the session it was registered from: it is only sent to while that
/// session still has a valid refresh token. A push token belongs to one person: registering it again moves it.
/// </summary>
public class UserDevice
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>The phone's or browser's session (<c>refresh_tokens.session_id</c>).</summary>
    public Guid SessionId { get; set; }

    public PushProvider Provider { get; set; }

    /// <summary>Expo's token, or for Web Push the SHA-256 of <see cref="WebPushEndpoint"/>, which is too long to index.</summary>
    public string PushToken { get; set; } = string.Empty;

    /// <summary>The browser's push service address. Set for Web Push only, with its two keys.</summary>
    public string? WebPushEndpoint { get; set; }

    /// <summary>The browser's public key, which the push's text is encrypted to.</summary>
    public string? WebPushP256dh { get; set; }

    /// <summary>The browser's authentication secret for the encryption.</summary>
    public string? WebPushAuth { get; set; }

    public DevicePlatform Platform { get; set; }

    public string? AppVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
