using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>The website's refresh tokens: when one presented again after its rotation counts as stolen, and which app's they are.</summary>
public static class RefreshTokenRules
{
    /// <summary>
    /// A rotation's answer can be lost (a phone that slept, a dropped connection, two tabs at once), and the browser then
    /// sends the old cookie again. Within this time that is an ordinary refusal; later, it is treated as theft and every
    /// session of the person ends.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    public static bool WithinReuseGrace(DateTime rotatedAt, DateTime now) => now - rotatedAt < ReuseGrace;

    /// <summary>
    /// The client of a website session's next token. A session the installed app started or refreshed stays the installed
    /// app's: on Android a browser tab shares its cookie, and that tab's refreshes never shorten the session again.
    /// </summary>
    public static RefreshTokenClient NextWebClient(RefreshTokenClient current, bool fromInstalledApp)
        => current == RefreshTokenClient.Installed || fromInstalledApp ? RefreshTokenClient.Installed : RefreshTokenClient.Web;
}
