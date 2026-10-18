using ID_Indetails.api.Enums;
using static ID_Indetails.api.Enums.PermissionSensitivity;
using P = ID_Indetails.api.Constants.PermissionCodes;

namespace ID_Indetails.api.Constants;

/// <summary>
/// The single source for which permissions exist and which role holds them. The initial
/// migration seeds <c>permissions</c> and <c>role_permissions</c> from this class.
/// Changing it changes seed data, so it needs approval like any database change.
/// </summary>
public static class PermissionCatalog
{
    /// <param name="DomainCode">
    /// Null when the permission applies in either domain. Otherwise the permission only
    /// takes effect for users of that domain (and for super admins inside it).
    /// </param>
    /// <param name="Sensitivity">What harm its actions could do (Proton confirms those strongly). Code only: not seeded.</param>
    public sealed record Definition(string Code, string Description, string? DomainCode = null, PermissionSensitivity Sensitivity = PermissionSensitivity.None);

    public static readonly IReadOnlyList<Definition> Definitions =
    [
        new(P.ProjectView, "View projects"),
        new(P.ProjectCreate, "Create projects"),
        new(P.ProjectEdit, "Edit project details, owner, client and revenue"),
        new(P.ProjectChangeStatus, "Put projects on hold, complete, reopen or archive them"),
        new(P.ProjectPricingManage, "Set the project fee, default material markup and material client prices", DomainCodes.Execution, Sensitivity: Money),
        new(P.StatementCreate, "Create client statements of materials supplied", DomainCodes.Execution, Sensitivity: Money),
        new(P.StatementSend, "Send client statements", DomainCodes.Execution, Sensitivity: Money),
        new(P.RequirementView, "View project requirements"),
        new(P.RequirementManage, "Add, edit, assign, tick and remove requirements"),
        new(P.RequirementCompleteOwn, "Tick requirements assigned to you, and untick the ones you ticked"),
        new(P.MeetingManage, "Record and remove project meetings"),
        new(P.MeetingCreate, "Set project meetings, and move or remove the ones you set"),
        new(P.ExpenseManage, "Add and remove project expenses", Sensitivity: Money),

        new(P.WorkEntryCreate, "Log, edit and delete own work entries", DomainCodes.Design),
        new(P.WorkEntryViewOwn, "View own work entries", DomainCodes.Design),
        new(P.WorkEntryViewAll, "View everyone's work entries", DomainCodes.Design),
        new(P.WorkEntryComment, "Comment on work entries", DomainCodes.Design),

        new(P.MaterialCreate, "Record materials on a site project", DomainCodes.Execution, Sensitivity: Money),
        new(P.MaterialManage, "Edit or remove anyone's material records", DomainCodes.Execution, Sensitivity: Money),
        new(P.LabourReportCreate, "Submit weekly ground-labour reports", DomainCodes.Execution, Sensitivity: Money),
        new(P.LabourReportManage, "Edit anyone's labour reports and labour defaults", DomainCodes.Execution, Sensitivity: Money),

        new(P.ClientView, "View clients"),
        new(P.ClientCreate, "Add clients"),
        new(P.ClientManage, "Edit, archive, restore and delete clients"),
        new(P.ProjectFilesManage, "Manage anyone's project files and start comments on them", DomainCodes.Design),
        new(P.ClientAccessManage, "Invite clients to a project's client portal and end their access", DomainCodes.Design, Sensitivity: Access),
        new(P.ClientPortalManage, "Choose which project files a client sees, and publish the client portal", DomainCodes.Design),

        new(P.FinancialView, "See revenue, cost, profit, margin and rates", Sensitivity: Money),
        new(P.InvoiceCreate, "Create and edit draft invoices", Sensitivity: Money),
        new(P.InvoiceSend, "Issue and resend invoices", Sensitivity: Money),
        new(P.InvoiceVoid, "Void issued invoices", Sensitivity: Money),
        new(P.PaymentRecord, "Record client payments", Sensitivity: Money),
        new(P.PaymentVoid, "Void recorded payments", Sensitivity: Money),
        new(P.BillingSettingsManage, "Edit invoice prefix, terms and bank details", Sensitivity: Money),
        new(P.ExchangeRateManage, "Set the company-wide exchange rates used for payments", Sensitivity: Money),

        new(P.EmployeeView, "View employees and their work"),
        new(P.EmployeeManage, "Edit employee profiles, teams and status", Sensitivity: Access),
        new(P.EmployeeInvite, "Invite new people", Sensitivity: Access),
        new(P.EmployeePayManage, "Set and change employee pay", Sensitivity: Pay),
        new(P.EmployeeInviteSuperAdmin, "Invite super admins", Sensitivity: Access),
        new(P.EmployeeChangeEmail, "Change sign-in addresses", Sensitivity: Access),
        new(P.SessionManage, "End people's sessions and restore their sign-in", Sensitivity: Access),
        new(P.PermissionManage, "Grant and revoke other people's permissions", Sensitivity: Access),
        new(P.TeamManage, "Create, rename and delete teams"),

        new(P.ActivityView, "View the domain activity feed"),
        new(P.ReportView, "View domain reports"),
        new(P.DomainSettingsManage, "Edit the domain's time-tracking and notification settings", Sensitivity: CompanyRules),

        new(P.CompanyView, "View the company overview across both domains"),
        new(P.OverheadManage, "Manage company overhead", Sensitivity: Money),
        new(P.OverheadCategoryManage, "Add, rename and remove company expense categories", Sensitivity: Money),
        new(P.SalaryView, "View what each employee is owed", Sensitivity: Pay),
        new(P.CompanySettingsManage, "Edit company-wide settings", Sensitivity: CompanyRules),
        new(P.WorkingTimeManage, "Set the company working week, break and overtime multiplier", Sensitivity: Pay),
        new(P.ScheduleManage, "Set personal work schedules", Sensitivity: Pay),
        new(P.HoursExcuse, "Excuse hours and days off in the hours ledger", Sensitivity: Pay),

        new(P.WebsiteView, "View the company website's content"),
        new(P.WebsiteManage, "Edit the company website: projects, services, locations, contact details and favicon"),

        // Admins and super admins only: employees don't get Proton yet.
        new(P.ProtonUse, "Ask Proton, the company assistant, which proposes changes only for the person to confirm"),
        new(P.ProtonBudgetManage, "See Proton's costs and usage, and top up its monthly budget"),
    ];

    private static readonly string[] CompanyOnly =
    [
        P.CompanyView, P.OverheadManage, P.SalaryView, P.CompanySettingsManage,
        P.WorkingTimeManage, P.ScheduleManage, P.HoursExcuse,
        P.EmployeeInviteSuperAdmin, P.PermissionManage, P.ExchangeRateManage,
        P.OverheadCategoryManage, P.SessionManage, P.EmployeeChangeEmail,
        P.ProtonBudgetManage, P.WebsiteView, P.WebsiteManage,
    ];

    /// <summary>
    /// Money and hiring are the super admins' alone: admins see no revenue, cost, profit, invoices, payments, pricing,
    /// statements or pay, and invite no one.
    /// </summary>
    private static readonly string[] SuperAdminOnly =
    [
        P.FinancialView, P.InvoiceCreate, P.InvoiceSend, P.InvoiceVoid, P.PaymentRecord, P.PaymentVoid,
        P.BillingSettingsManage, P.ExpenseManage, P.ProjectPricingManage, P.StatementCreate, P.StatementSend,
        P.EmployeeInvite, P.EmployeePayManage,
    ];

    /// <summary>
    /// What clients are shown is the super admins' to choose and publish, or whoever they grant it to. Admins still see
    /// the client portal through client access.
    /// </summary>
    private static readonly string[] ClientPublishing = [P.ClientPortalManage];

    /// <summary>Logging one's own hours: employees only. Admins and super admins are paid a fixed salary and log no hours.</summary>
    public static readonly IReadOnlyList<string> OwnWork = [P.WorkEntryCreate, P.WorkEntryViewOwn];

    private static readonly string[] EmployeeGrants =
    [
        P.ProjectView,
        P.RequirementView,
        P.RequirementCompleteOwn,
        P.MeetingCreate,
        P.WorkEntryCreate,
        P.WorkEntryViewOwn,
        P.MaterialCreate,
        P.LabourReportCreate,
        P.ProtonUse,
    ];

    /// <summary>Permission codes held by each role.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Grants =
        new Dictionary<string, IReadOnlyList<string>>
        {
            // Everything, in both domains and at company level, except logging their own hours.
            [RoleCodes.SuperAdmin] = Definitions.Select(d => d.Code).Except(OwnWork).ToList(),
            // Their own domain's work, without its money, hiring or client publishing; nothing at company level, and no
            // hours of their own.
            [RoleCodes.Admin] = Definitions.Select(d => d.Code)
                .Except(CompanyOnly).Except(SuperAdminOnly).Except(ClientPublishing).Except(OwnWork).ToList(),
            // Their own work only; never money.
            [RoleCodes.Employee] = EmployeeGrants,
        };
}
