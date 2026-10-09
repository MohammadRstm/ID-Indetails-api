using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>What a refresh token the phone app presents leads to (docs/authentication.md, "Mobile sessions").</summary>
public enum MobileRefreshCase
{
    /// <summary>Unknown, expired, signed out or revoked: refused, nothing else changes.</summary>
    Refused,

    /// <summary>Valid: replaced by a new token in the same session.</summary>
    Rotate,

    /// <summary>
    /// Presented again soon after its rotation while nothing after it was ever presented: the answer was lost. The last
    /// unused token is superseded and a new one is issued in the same session.
    /// </summary>
    LostAnswerRetry,

    /// <summary>Presented again after the session reached its end: refused, nothing else changes.</summary>
    SessionExpired,

    /// <summary>
    /// Presented again after the grace while nothing after it was presented: an old token can't be told from a phone
    /// that was offline a long time, so it is refused and only this session ends.
    /// </summary>
    StaleReplay,

    /// <summary>Two parties hold the session: every session of the person ends, on every device.</summary>
    Theft,
}

/// <summary>
/// The phone's refresh rules. A refresh can succeed while its answer is lost on a weak connection, and the phone then
/// presents the old token again; within the grace that is answered with a new token instead of being taken for theft.
/// Theft is still caught whenever a token after the presented one was itself used, or a superseded one comes back.
/// </summary>
public static class MobileRefreshRules
{
    /// <summary>The state of one refresh token of a mobile session.</summary>
    public sealed record Token(DateTime ExpiresAt, DateTime? RevokedAt, RevokeReason? Reason);

    /// <param name="presented">The token the phone presented.</param>
    /// <param name="after">
    /// The tokens after it along <c>replaced_by_id</c>, in order, walking past superseded ones and stopping at the first
    /// that isn't superseded (included): that one is the session's last token.
    /// </param>
    public static MobileRefreshCase Decide(Token presented, IReadOnlyList<Token> after, DateTime now, TimeSpan grace)
    {
        if (presented.RevokedAt is not { } rotatedAt)
        {
            return presented.ExpiresAt > now ? MobileRefreshCase.Rotate : MobileRefreshCase.Refused;
        }

        if (presented.Reason == RevokeReason.Superseded)
        {
            // only ever held by someone other than the phone that retried, which never received it
            return MobileRefreshCase.Theft;
        }

        if (presented.Reason != RevokeReason.Rotated || after.Count == 0)
        {
            return MobileRefreshCase.Refused;
        }

        if (after.Any(t => t.Reason == RevokeReason.Rotated))
        {
            return MobileRefreshCase.Theft;
        }

        var last = after[^1];
        if (last.RevokedAt is not null)
        {
            // the session was signed out or ended meanwhile
            return MobileRefreshCase.Refused;
        }

        if (last.ExpiresAt <= now)
        {
            return MobileRefreshCase.SessionExpired;
        }

        return now - rotatedAt <= grace ? MobileRefreshCase.LostAnswerRetry : MobileRefreshCase.StaleReplay;
    }
}
