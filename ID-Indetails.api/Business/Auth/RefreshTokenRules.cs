namespace ID_Indetails.api.Business.Auth;

/// <summary>When a refresh token presented again after its rotation counts as stolen.</summary>
public static class RefreshTokenRules
{
    /// <summary>
    /// A rotation's answer can be lost (a phone that slept, a dropped connection, two tabs at once), and the browser then
    /// sends the old cookie again. Within this time that is an ordinary refusal; later, it is treated as theft and every
    /// session of the person ends.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    public static bool WithinReuseGrace(DateTime rotatedAt, DateTime now) => now - rotatedAt < ReuseGrace;
}
