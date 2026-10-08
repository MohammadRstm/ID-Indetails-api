namespace ID_Indetails.api.Configuration.Options;

/// <summary>Pushes to the phone app (docs/notifications.md, "Pushes").</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>The daily removal of devices whose session has ended. Integration tests switch it off.</summary>
    public bool DeviceCleanupEnabled { get; init; } = true;
}
