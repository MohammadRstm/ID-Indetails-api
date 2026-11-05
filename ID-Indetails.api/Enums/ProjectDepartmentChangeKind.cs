namespace ID_Indetails.api.Enums;

/// <summary>Why a project's split between its departments changed.</summary>
public enum ProjectDepartmentChangeKind
{
    /// <summary>The departments chosen when the project was made, in equal shares.</summary>
    Created,
    /// <summary>Departments added or removed: the shares went back to equal.</summary>
    Departments,
    /// <summary>The shares edited by hand.</summary>
    Shares,
}
