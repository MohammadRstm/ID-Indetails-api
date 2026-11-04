namespace ID_Indetails.api.DTO.Request.Auth;

public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Sent by the website installed as an app: its session lasts 30 days since last use (<c>RefreshTokenClient.Installed</c>).
    /// The phone app's sign-in ignores it.
    /// </summary>
    public bool InstalledApp { get; init; }
}
