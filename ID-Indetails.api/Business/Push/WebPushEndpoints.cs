using System.Security.Cryptography;
using System.Text;

namespace ID_Indetails.api.Business.Push;

/// <summary>
/// Where a browser's pushes may be posted. The API posts to whatever address the browser registered, so only the
/// browsers' own push services are allowed: the API must never be made to call another server, an internal one least of all.
/// </summary>
public static class WebPushEndpoints
{
    /// <summary>Google's (Chrome and most Android browsers), Apple's (Safari) and Mozilla's (Firefox).</summary>
    private static readonly string[] Hosts = ["fcm.googleapis.com", "web.push.apple.com", "updates.push.services.mozilla.com"];

    /// <summary>Microsoft's (Edge), on numbered hosts such as <c>wns2-par02p.notify.windows.com</c>.</summary>
    private const string MicrosoftHostSuffix = ".notify.windows.com";

    public static bool IsAllowed(string endpoint)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.UserInfo.Length == 0
            && (Hosts.Contains(uri.Host) || uri.Host.EndsWith(MicrosoftHostSuffix, StringComparison.Ordinal));

    /// <summary>The device's push token: the endpoint's SHA-256, since the endpoint is too long for the unique index.</summary>
    public static string PushTokenOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}
