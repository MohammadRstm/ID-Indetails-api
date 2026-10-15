namespace ID_Indetails.api.Enums;

/// <summary>Where a client's portal account stands.</summary>
public enum PortalAccountStatus
{
    /// <summary>Invited to a project; no password yet.</summary>
    Invited,

    /// <summary>Set a password through an invitation and can sign in.</summary>
    Active,
}
