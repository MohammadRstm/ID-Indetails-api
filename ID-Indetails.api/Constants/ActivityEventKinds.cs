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

    // People (subject: the user)
    public const string EmployeeUpdated = "employee_updated";
    public const string EmployeeDeactivated = "employee_deactivated";
    public const string EmployeeReactivated = "employee_reactivated";
    public const string RoleChanged = "role_changed";
    public const string EmployeeMoved = "employee_moved";
    public const string PayChanged = "pay_changed";

    // Teams (subject: the team)
    public const string TeamCreated = "team_created";
    public const string TeamRenamed = "team_renamed";
    public const string TeamDeleted = "team_deleted";
}
