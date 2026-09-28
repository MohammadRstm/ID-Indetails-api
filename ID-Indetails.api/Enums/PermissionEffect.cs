namespace ID_Indetails.api.Enums;

/// <summary>What a personal permission override does to a user's role defaults.</summary>
public enum PermissionEffect
{
    /// <summary>Adds a permission the role does not give.</summary>
    Grant,

    /// <summary>Takes away a permission the role gives.</summary>
    Revoke,
}
