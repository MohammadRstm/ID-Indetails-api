namespace ID_Indetails.api.Enums;

/// <summary>Which clients the client list keeps.</summary>
public enum ClientFilter
{
    /// <summary>With an active or on-hold project.</summary>
    Active,

    /// <summary>Still owed on their invoices. Needs <c>financial:view</c>.</summary>
    Owing,

    /// <summary>With no projects at all.</summary>
    Empty,
}
