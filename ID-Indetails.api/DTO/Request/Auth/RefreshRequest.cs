namespace ID_Indetails.api.DTO.Request.Auth;

/// <summary>The website's refresh. The body is optional: the refresh token itself is the cookie.</summary>
public sealed record RefreshRequest
{
    /// <summary>Sent by the website installed as an app: from then on its session lasts 30 days since last use.</summary>
    public bool InstalledApp { get; init; }
}
