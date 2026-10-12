namespace ID_Indetails.api.DTO.Request.Auth;

/// <summary>The phone app's refresh token, sent in the body: the app has no cookie.</summary>
public sealed record MobileRefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}
