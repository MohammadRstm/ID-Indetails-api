namespace ID_Indetails.api.Enums;

/// <summary>Where one of the client's email contacts stands on a project's client portal.</summary>
public enum PortalClientStatus
{
    NotInvited,

    /// <summary>An invitation is waiting to be accepted.</summary>
    Invited,

    /// <summary>The last invitation ran out before it was accepted.</summary>
    InvitationExpired,

    /// <summary>Signed up and has access to the project.</summary>
    Active,

    /// <summary>Had access, which was ended.</summary>
    AccessEnded,
}
