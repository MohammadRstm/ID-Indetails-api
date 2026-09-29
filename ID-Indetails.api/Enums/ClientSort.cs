namespace ID_Indetails.api.Enums;

/// <summary>How the client list is ordered. The money sorts need <c>financial:view</c>.</summary>
public enum ClientSort
{
    /// <summary>By display name, A to Z.</summary>
    Name,

    /// <summary>Most projects first.</summary>
    Projects,

    /// <summary>Most logged time first.</summary>
    Hours,

    /// <summary>Most recent work first; a client without work counts from when it was added.</summary>
    Activity,

    Revenue,

    Profit,

    /// <summary>Best margin first; clients whose margin isn't known yet last.</summary>
    Margin,

    Outstanding,
}
