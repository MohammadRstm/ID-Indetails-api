namespace ID_Indetails.api.DTO.Response.Auth;

/// <summary>
/// Returned by login, refresh, change-password and invitation acceptance. The refresh token
/// is never in the body: it travels only in the HttpOnly cookie.
/// </summary>
public sealed record AuthResponse
{
    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required CurrentUserResponse User { get; init; }
}
