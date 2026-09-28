namespace ID_Indetails.api.Constants;

/// <summary>Values of <c>notifications.kind</c> written by the API.</summary>
public static class NotificationKinds
{
    /// <summary>Someone was assigned a requirement (subject: the requirement).</summary>
    public const string RequirementAssigned = "requirement_assigned";

    /// <summary>Someone recorded materials on a site (subject: the project; the count in the payload).</summary>
    public const string MaterialsAdded = "materials_added";
}
