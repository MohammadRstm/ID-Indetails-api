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

    // Clients (subject: the client)
    public const string ClientAdded = "client_added";
    public const string ClientUpdated = "client_updated";
    public const string ClientArchived = "client_archived";
    public const string ClientRestored = "client_restored";
    public const string ClientDeleted = "client_deleted";

    // Projects (subject: the project). Money goes in the amount columns, never the payload.
    public const string ProjectCreated = "project_created";
    public const string ProjectUpdated = "project_updated";
    public const string ProjectOwnerSet = "project_owner_set";
    public const string ProjectClientSet = "project_client_set";
    public const string RevenueSet = "revenue_set";
    public const string RevenueChanged = "revenue_changed";
    public const string ProjectFeeSet = "project_fee_set";
    public const string MaterialMarkupSet = "material_markup_set";
}
