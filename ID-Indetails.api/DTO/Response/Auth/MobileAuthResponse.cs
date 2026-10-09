namespace ID_Indetails.api.DTO.Response.Auth;

/// <summary>
/// Returned by the phone app's sign-in, refresh and change-password. Unlike <see cref="AuthResponse"/> it carries the
/// refresh token, which the app keeps in the phone's secure store.
/// </summary>
public sealed record MobileAuthResponse
{
    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAt { get; init; }

    public required CurrentUserResponse User { get; init; }
}
