namespace ID_Indetails.api.Enums;

/// <summary>Which app a refresh token belongs to. Each has its own lifetime and replay rules (docs/authentication.md).</summary>
public enum RefreshTokenClient
{
    /// <summary>The website: the token travels in the HttpOnly cookie, lasts a day and has the 30-second reuse rule.</summary>
    Web,

    /// <summary>The phone app: the token travels in the body, lasts 30 days since last use and has the replay grace.</summary>
    Mobile,
}
