namespace ID_Indetails.api.Enums;

/// <summary>Which app a refresh token belongs to. Each has its own lifetime and replay rules (docs/authentication.md).</summary>
public enum RefreshTokenClient
{
    /// <summary>The website: the token travels in the HttpOnly cookie, lasts a day and has the 30-second reuse rule.</summary>
    Web,

    /// <summary>The phone app: the token travels in the body, lasts 30 days since last use and has the replay grace.</summary>
    Mobile,

    /// <summary>
    /// The website installed as an app on a phone: the cookie and the reuse rule of <see cref="Web"/>, but it lasts 30 days
    /// since last use, like the phone app, so its pushes keep coming while it isn't opened.
    /// </summary>
    Installed,
}
