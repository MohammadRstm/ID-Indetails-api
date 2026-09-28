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
    public sealed record Definition(string Code, string Description, string? DomainCode = null);

    public static readonly IReadOnlyList<Definition> Definitions =
    [
        new(P.ProjectView, "View projects"),
        new(P.ProjectCreate, "Create projects"),
        new(P.ProjectEdit, "Edit project details, owner, client and revenue"),
        new(P.ProjectChangeStatus, "Put projects on hold, complete, reopen or archive them"),
        new(P.ProjectPricingManage, "Set the project fee, default material markup and material client prices", DomainCodes.Execution),
        new(P.StatementCreate, "Create client statements of materials supplied", DomainCodes.Execution),
        new(P.StatementSend, "Send client statements", DomainCodes.Execution),
        new(P.RequirementView, "View project requirements"),
        new(P.RequirementManage, "Add, edit, assign, tick and remove requirements"),
        new(P.MeetingManage, "Record and remove project meetings", DomainCodes.Design),
        new(P.ExpenseManage, "Add and remove project expenses"),

        new(P.WorkEntryCreate, "Log, edit and delete own work entries", DomainCodes.Design),
        new(P.WorkEntryViewOwn, "View own work entries", DomainCodes.Design),
        new(P.WorkEntryViewAll, "View everyone's work entries", DomainCodes.Design),
        new(P.WorkEntryComment, "Comment on work entries", DomainCodes.Design),

        new(P.MaterialCreate, "Record materials on a site project", DomainCodes.Execution),
        new(P.MaterialManage, "Edit or remove anyone's material records", DomainCodes.Execution),
        new(P.LabourReportCreate, "Submit weekly ground-labour reports", DomainCodes.Execution),
        new(P.LabourReportManage, "Edit anyone's labour reports and labour defaults", DomainCodes.Execution),

        new(P.ClientView, "View clients"),
        new(P.ClientCreate, "Add clients"),
        new(P.ClientManage, "Edit, archive, restore and delete clients"),

        new(P.FinancialView, "See revenue, cost, profit, margin and rates"),
        new(P.InvoiceCreate, "Create and edit draft invoices"),
        new(P.InvoiceSend, "Issue and resend invoices"),
        new(P.InvoiceVoid, "Void issued invoices"),
        new(P.PaymentRecord, "Record client payments"),
        new(P.PaymentVoid, "Void recorded payments"),
        new(P.BillingSettingsManage, "Edit invoice prefix, terms and bank details"),

        new(P.EmployeeView, "View employees and their work"),
        new(P.EmployeeManage, "Edit employee profiles, teams and status"),
        new(P.EmployeeInvite, "Invite new people"),
        new(P.EmployeePayManage, "Set and change employee pay"),

        new(P.ActivityView, "View the domain activity feed"),
        new(P.ReportView, "View domain reports"),
        new(P.DomainSettingsManage, "Edit the domain's time-tracking and notification settings"),

        new(P.CompanyView, "View the company overview across both domains"),
        new(P.OverheadManage, "Manage company overhead"),
        new(P.SalaryView, "View what each employee is owed"),
        new(P.CompanySettingsManage, "Edit company-wide settings"),
        new(P.WorkingTimeManage, "Set the company working week, break and overtime multiplier"),
        new(P.ScheduleManage, "Set personal work schedules"),
        new(P.HoursExcuse, "Excuse hours and days off in the hours ledger"),
    ];

    private static readonly string[] CompanyOnly =
    [
        P.CompanyView, P.OverheadManage, P.SalaryView, P.CompanySettingsManage,
        P.WorkingTimeManage, P.ScheduleManage, P.HoursExcuse,
    ];

    private static readonly string[] EmployeeGrants =
    [
        P.ProjectView,
        P.RequirementView,
        P.WorkEntryCreate,
        P.WorkEntryViewOwn,
        P.MaterialCreate,
        P.LabourReportCreate,
    ];

    /// <summary>Permission codes held by each role.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Grants =
        new Dictionary<string, IReadOnlyList<string>>
        {
            // Everything, in both domains and at company level.
            [RoleCodes.SuperAdmin] = Definitions.Select(d => d.Code).ToList(),
            // Everything inside their own domain, nothing at company level.
            [RoleCodes.Admin] = Definitions.Select(d => d.Code).Except(CompanyOnly).ToList(),
            // Their own work only; never money.
            [RoleCodes.Employee] = EmployeeGrants,
        };
}
