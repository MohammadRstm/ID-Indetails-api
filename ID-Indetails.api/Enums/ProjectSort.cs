namespace ID_Indetails.api.Enums;

/// <summary>How the project list is ordered.</summary>
public enum ProjectSort
{
    /// <summary>Newest start first.</summary>
    Start,

    /// <summary>Most recent work first; a project with none counts from when it was added.</summary>
    Activity,

    /// <summary>Most logged time first.</summary>
    Hours,

    /// <summary>Furthest along first.</summary>
    Progress,

    /// <summary>By title, A to Z.</summary>
    Name,

    /// <summary>By client, A to Z; projects without a client last.</summary>
    Client,
}
