using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>Who can reset a forgotten password, and when a reset link still works.</summary>
public static class PasswordResetRules
{
    /// <summary>
    /// Only active accounts: invited people choose their password through their invitation, and deactivated people
    /// can't sign in at all.
    /// </summary>
    public static bool CanReset(UserStatus status) => status == UserStatus.Active;

    /// <summary>A link works once, until it expires, and only while its account can still reset.</summary>
    public static bool IsUsable(DateTime? usedAt, DateTime expiresAt, UserStatus status, DateTime now)
        => usedAt is null && expiresAt > now && CanReset(status);
}
