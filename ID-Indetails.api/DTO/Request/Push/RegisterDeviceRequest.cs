using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Push;

/// <summary>This phone's push token, as Expo issued it.</summary>
public sealed record RegisterDeviceRequest
{
    public string PushToken { get; init; } = string.Empty;

    public DevicePlatform? Platform { get; init; }

    public string? AppVersion { get; init; }
}
