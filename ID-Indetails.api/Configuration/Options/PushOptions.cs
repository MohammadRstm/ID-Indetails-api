namespace ID_Indetails.api.Configuration.Options;

/// <summary>Pushes to the phone app and browsers (docs/notifications.md, "Pushes"). Off unless <see cref="Enabled"/> is set.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Nothing is queued or sent while this is off.</summary>
    public bool Enabled { get; init; }

    public ExpoPushOptions Expo { get; init; } = new();

    public WebPushOptions WebPush { get; init; } = new();

    /// <summary>The daily removal of devices whose session has ended. Integration tests switch it off.</summary>
    public bool DeviceCleanupEnabled { get; init; } = true;
}

public sealed class ExpoPushOptions
{
    /// <summary>
    /// Expo's access token ("enhanced push security"). A secret: empty in appsettings, set with user secrets locally and the
    /// <c>Push__Expo__AccessToken</c> environment variable in production. Without it pushes are sent unauthenticated.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;
}

/// <summary>
/// The server's VAPID key pair, which push services check every browser push against (base64url, as VAPID tools write
/// them). Browsers get no pushes, and aren't offered them, until both are set. A new pair means everyone turns their
/// browser's notifications on again.
/// </summary>
public sealed class WebPushOptions
{
    /// <summary>Not a secret: browsers subscribe with it.</summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// A secret: empty in appsettings, set with user secrets locally and the <c>Push__WebPush__PrivateKey</c> environment
    /// variable in production.
    /// </summary>
    public string PrivateKey { get; init; } = string.Empty;

    public bool Configured => PublicKey.Length > 0 && PrivateKey.Length > 0;
}
