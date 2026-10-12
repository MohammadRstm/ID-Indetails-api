using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Push;

/// <summary>A phone that receives the caller's pushes. The push token itself is never returned.</summary>
/// <param name="Current">The phone this request came from.</param>
public sealed record DeviceResponse(
    long Id,
    DevicePlatform Platform,
    string? AppVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Current);
