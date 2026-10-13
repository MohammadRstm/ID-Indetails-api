using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// What each permission lets someone do, in a sentence or two, for the permission lists people read. The catalog's
/// own description is the short name; this says what it opens and what it doesn't.
/// </summary>
public static class PermissionExplanations
{
    public static string Of(string code) => All.GetValueOrDefault(code, string.Empty);

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        // Projects
        [PermissionCodes.ProjectView] = "Open the business's projects: their details, timeline, meetings and, on sites, the labour weeks. Money on them needs \"See revenue, cost, profit, margin and rates\" too.",
        [PermissionCodes.ProjectCreate] = "Start new projects in the business, for a client or internal.",
        [PermissionCodes.ProjectEdit] = "Change a project's name, description, dates, owner, client and the amount the client pays.",
        [PermissionCodes.ProjectChangeStatus] = "Put a project on hold, resume it, mark it completed, reopen it or archive it.",
        [PermissionCodes.ProjectPricingManage] = "On sites: set the project fee, the default markup on materials and the price each material is charged to the client.",

        // Requirements and meetings
        [PermissionCodes.RequirementView] = "See the list of requirements on each project: what has to be done, by whom and by when.",
        [PermissionCodes.RequirementManage] = "Add, edit, assign and remove requirements, and tick or untick anyone's.",
        [PermissionCodes.RequirementCompleteOwn] = "Tick the requirements assigned to you when they are done, and untick the ones you ticked.",
        [PermissionCodes.MeetingCreate] = "Set meetings on projects, and move or remove the meetings you set.",
        [PermissionCodes.MeetingManage] = "Record, move and remove anyone's project meetings.",

        // Work
        [PermissionCodes.WorkEntryCreate] = "Log the hours you work on projects, and edit or delete your own entries.",
        [PermissionCodes.WorkEntryViewOwn] = "See your own work entries, totals and hours balance.",
        [PermissionCodes.WorkEntryViewAll] = "See everyone's work entries and totals, today's activity, overtime, and export timesheets.",
        [PermissionCodes.WorkEntryComment] = "Leave comments on people's work entries.",
        [PermissionCodes.ExpenseManage] = "Add and remove the expenses charged to a project, such as printing or travel.",

        // Sites
        [PermissionCodes.MaterialCreate] = "Record the materials bought for a site, with their supplier and cost.",
        [PermissionCodes.MaterialManage] = "Edit or remove material records, including ones other people added.",
        [PermissionCodes.LabourReportCreate] = "Submit the weekly report of ground labour on a site: who worked and for how many days.",
        [PermissionCodes.LabourReportManage] = "Edit anyone's labour reports and change the labour defaults.",
        [PermissionCodes.StatementCreate] = "Prepare client statements listing the materials supplied to a site.",
        [PermissionCodes.StatementSend] = "Send client statements, by email, on WhatsApp or as a PDF. A material's price counts as revenue once a statement sends it.",

        // Clients
        [PermissionCodes.ClientView] = "See the business's clients and their contact details.",
        [PermissionCodes.ClientCreate] = "Add new clients, with their contact details, so projects can be made for them.",
        [PermissionCodes.ClientManage] = "Edit clients' details and contacts, archive or restore them, and delete clients with no projects.",
        [PermissionCodes.ClientAccessManage] = "On Design projects: invite the client's contacts to the client portal by email, resend or cancel invitations, and end a client's access to the project.",

        // Money
        [PermissionCodes.FinancialView] = "See the money: projects' revenue, cost, profit and margin, people's cost rates, invoices and payments. Without it, these are hidden everywhere.",
        [PermissionCodes.InvoiceCreate] = "Write draft invoices for client projects and edit or discard them before they are sent.",
        [PermissionCodes.InvoiceSend] = "Issue invoices, which numbers them and starts the payment clock, send them to clients, and schedule them to go out on their own.",
        [PermissionCodes.InvoiceVoid] = "Cancel an issued invoice with a reason, while no payment is attached. Its number is kept.",
        [PermissionCodes.PaymentRecord] = "Record the money clients pay, against an invoice or on account.",
        [PermissionCodes.PaymentVoid] = "Cancel a recorded payment with a reason, for one recorded by mistake or a cheque that bounced.",
        [PermissionCodes.BillingSettingsManage] = "Set up invoicing for the business: the invoice number prefix, default payment terms and the bank details printed on invoices.",
        [PermissionCodes.ExchangeRateManage] = "Set the dollar exchange rates used to value payments in other currencies, for the whole company.",

        // People
        [PermissionCodes.EmployeeView] = "See the people in the business, their profiles and their work.",
        [PermissionCodes.EmployeeInvite] = "Invite new admins and employees to the business, and resend their invitations.",
        [PermissionCodes.EmployeeInviteSuperAdmin] = "Invite super admins, and change super admins' accounts.",
        [PermissionCodes.EmployeeChangeEmail] = "Change the address people sign in with, their own included (with their password). It ends their sessions; a super admin's also needs \"Invite super admins\".",
        [PermissionCodes.SessionManage] = "Sign someone out everywhere at once and block their sign-in until it is restored, then restore it. Never your own; a super admin's also needs \"Invite super admins\".",
        [PermissionCodes.EmployeeManage] = "Edit people's profiles, teams and positions, change their role or business, and deactivate or reactivate their accounts.",
        [PermissionCodes.EmployeePayManage] = "See and change what people are paid, from a date onwards.",
        [PermissionCodes.PermissionManage] = "Give people permissions beyond their role or take some away. Anyone but a super admin can only hand out permissions they hold.",
        [PermissionCodes.TeamManage] = "Create, rename and delete the business's teams.",

        // The business
        [PermissionCodes.ActivityView] = "See the business's activity feed: who did what, and when.",
        [PermissionCodes.ReportView] = "Open the dashboard and the reports: work by project, person and client, and upcoming deadlines.",
        [PermissionCodes.DomainSettingsManage] = "Change the business's settings: time tracking, notifications and when finished projects are archived.",

        // The company
        [PermissionCodes.CompanyView] = "See the company area: the overview and finances of both businesses together.",
        [PermissionCodes.OverheadManage] = "Manage the company's own expenses, such as rent and software.",
        [PermissionCodes.OverheadCategoryManage] = "Add, rename and remove the categories the company's expenses are sorted into. A category still in use can't be removed.",
        [PermissionCodes.SalaryView] = "See what each person is owed each month, from their pay and hours.",
        [PermissionCodes.CompanySettingsManage] = "Change the company's name, logo, address and other company-wide settings.",
        [PermissionCodes.WorkingTimeManage] = "Set the company's working week, daily break and overtime rate.",
        [PermissionCodes.ScheduleManage] = "Set people's personal work schedules, when they differ from the company's working week.",
        [PermissionCodes.HoursExcuse] = "Excuse hours or days off in people's hours balance, so they don't count as missing.",

        // Proton
        [PermissionCodes.ProtonUse] = "Ask Proton about the company and have it prepare changes. It sees only what this person could see in Hourline themselves, and changes nothing until they confirm.",
        [PermissionCodes.ProtonBudgetManage] = "See what Proton costs and who uses it, and top up its monthly budget.",
    };
}
