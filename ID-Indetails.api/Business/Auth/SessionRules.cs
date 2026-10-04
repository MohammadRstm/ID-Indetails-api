namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Whether an access token still opens the API once a super admin may have ended the person's sessions: never while
/// their sign-in is suspended, and never a token issued before their sessions were ended, even after a restore.
/// </summary>
public static class SessionRules
{
    public const string SuspendedMessage = "A super admin has ended your sessions. You can sign in again once they restore your access.";

    /// <param name="tokenIssuedAt">The token's <c>iat</c>, which is whole seconds: a token from the very second the
    /// sessions were ended may be older than the end, so it is refused too.</param>
    public static bool Accepts(DateTime tokenIssuedAt, DateTime? sessionsRevokedAt, DateTime? sessionsValidFrom)
        => sessionsRevokedAt is null
            && (sessionsValidFrom is not { } from || tokenIssuedAt >= WholeSecond(from).AddSeconds(1));

    /// <summary>
    /// A <c>sessions_valid_from</c> that ends every access token issued before <paramref name="now"/> but keeps the one
    /// handed out to the person making the change, in this very second.
    /// </summary>
    public static DateTime CutOffKeepingThisSecond(DateTime now) => now.AddSeconds(-1);

    private static DateTime WholeSecond(DateTime at) => at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond));
}
