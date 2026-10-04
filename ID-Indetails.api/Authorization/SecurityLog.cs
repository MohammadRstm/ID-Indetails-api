using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Authorization;

/// <summary>The log category security events go to (refused sign-ins, rate limits, replayed tokens), to find and alert on them together.</summary>
public static class SecurityLog
{
    public const string Category = "ID_Indetails.api.Security";

    /// <summary>"user 12" when signed in, otherwise the client's address (forwarded headers are trusted from the proxy only).</summary>
    public static string Caller(HttpContext context)
        => context.User.FindFirst(AuthClaimTypes.UserId)?.Value is { } id ? $"user {id}" : $"ip {context.Connection.RemoteIpAddress}";
}
