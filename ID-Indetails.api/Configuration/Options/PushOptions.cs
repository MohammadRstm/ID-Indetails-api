namespace ID_Indetails.api.Configuration.Options;

/// <summary>Pushes to the phone app (docs/notifications.md, "Pushes"). Off unless <see cref="Enabled"/> is set.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Nothing is queued or sent while this is off.</summary>
    public bool Enabled { get; init; }

    public ExpoPushOptions Expo { get; init; } = new();

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
