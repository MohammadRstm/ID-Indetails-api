namespace ID_Indetails.api.Enums;

/// <summary>Why a refresh token stopped being valid. Older rows have none.</summary>
public enum RevokeReason
{
    /// <summary>Presented and replaced by a normal refresh.</summary>
    Rotated,

    /// <summary>Issued but never presented, and replaced because the token before it was presented again (a lost answer).</summary>
    Superseded,

    /// <summary>Its session was signed out.</summary>
    SignedOut,

    /// <summary>Ended by a password change or reset, a super admin, deactivation, a new sign-in address, theft or a stale replay.</summary>
    Revoked,
}
