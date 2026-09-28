namespace ID_Indetails.api.Constants;

/// <summary>
/// Permission codes in <c>resource:action</c> form. Codes that already exist in the React
/// client's <c>config/permissions.ts</c> keep exactly the same string.
/// </summary>
public static class PermissionCodes
{
    // Projects
    public const string ProjectView = "project:view";
    public const string ProjectCreate = "project:create";
    public const string ProjectEdit = "project:edit";
    public const string ProjectChangeStatus = "project:change_status";
    public const string ProjectPricingManage = "project:pricing_manage";
    public const string StatementCreate = "statement:create";
    public const string StatementSend = "statement:send";
    public const string RequirementView = "requirement:view";
    public const string RequirementManage = "requirement:manage";
    public const string MeetingManage = "meeting:manage";
    public const string MeetingCreate = "meeting:create";
    public const string ExpenseManage = "expense:manage";

    // Design work tracking
    public const string WorkEntryCreate = "work_entry:create";
    public const string WorkEntryViewOwn = "work_entry:view_own";
    public const string WorkEntryViewAll = "work_entry:view_all";
    public const string WorkEntryComment = "work_entry:comment";

    // Execution site costs
    public const string MaterialCreate = "material:create";
    public const string MaterialManage = "material:manage";
    public const string LabourReportCreate = "labour_report:create";
    public const string LabourReportManage = "labour_report:manage";

    // Clients
    public const string ClientView = "client:view";
    public const string ClientCreate = "client:create";
    public const string ClientManage = "client:manage";

    // Money
    public const string FinancialView = "financial:view";
    public const string InvoiceCreate = "invoice:create";
    public const string InvoiceSend = "invoice:send";
    public const string InvoiceVoid = "invoice:void";
    public const string PaymentRecord = "payment:record";
    public const string PaymentVoid = "payment:void";
    public const string BillingSettingsManage = "billing_settings:manage";
    public const string ExchangeRateManage = "exchange_rate:manage";

    // People
    public const string EmployeeView = "employee:view";
    public const string EmployeeManage = "employee:manage";
    public const string EmployeeInvite = "employee:invite";
    public const string EmployeePayManage = "employee:pay_manage";
    public const string EmployeeInviteSuperAdmin = "employee:invite_super_admin";
    public const string PermissionManage = "permission:manage";
    public const string TeamManage = "team:manage";

    // Domain workspace
    public const string ActivityView = "activity:view";
    public const string ReportView = "report:view";
    public const string DomainSettingsManage = "domain_settings:manage";

    // Company layer (super admin)
    public const string CompanyView = "company:view";
    public const string OverheadManage = "overhead:manage";
    public const string SalaryView = "salary:view";
    public const string CompanySettingsManage = "company_settings:manage";
    public const string WorkingTimeManage = "working_time:manage";
    public const string ScheduleManage = "schedule:manage";
    public const string HoursExcuse = "hours:excuse";
}
