namespace ID_Indetails.api.Enums;

/// <summary>How a Design variation order's amount is divided between the project's departments.</summary>
public enum DesignVoSplit
{
    /// <summary>By the project's shares when it is applied, kept as they were then.</summary>
    Shares,
    /// <summary>Amounts given to chosen departments, adding up to its total.</summary>
    Departments,
}
