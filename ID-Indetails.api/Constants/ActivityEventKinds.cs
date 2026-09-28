namespace ID_Indetails.api.Constants;

/// <summary>Values of <c>activity_events.kind</c> written by the API.</summary>
public static class ActivityEventKinds
{
    // Permissions (subject: the user whose permissions changed)
    public const string PermissionGranted = "permission_granted";
    public const string PermissionRevoked = "permission_revoked";
    public const string PermissionReset = "permission_reset";
    public const string RoleAutoPromoted = "role_auto_promoted";
    public const string RoleAutoDemoted = "role_auto_demoted";
}
