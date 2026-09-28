namespace ID_Indetails.api.Enums;

/// <summary>One requested change to a user's permissions.</summary>
public enum PermissionChangeAction
{
    /// <summary>The user should hold the permission.</summary>
    Grant,

    /// <summary>The user should not hold the permission.</summary>
    Revoke,

    /// <summary>Back to whatever the user's role gives.</summary>
    Reset,
}
