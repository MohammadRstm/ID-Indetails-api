namespace ID_Indetails.api.Constants;

/// <summary>
/// Access levels. The strings must match the React client's <c>config/roles.ts</c>.
/// </summary>
public static class RoleCodes
{
    public const string SuperAdmin = "super_admin";
    public const string Admin = "admin";
    public const string Employee = "employee";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Employee];
}
