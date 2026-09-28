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
    public const string ProjectHold = "project_hold";
    public const string ProjectResumed = "project_resumed";
    public const string ProjectCompleted = "project_completed";
    public const string ProjectArchived = "project_archived";
    public const string ProjectReopened = "project_reopened";

    /// <summary>Revenue locked; <c>amount</c> is the revenue frozen at that moment.</summary>
    public const string ProjectFinalized = "project_finalized";

    /// <summary><c>amount</c> and <c>prev_amount</c> hold the new and previous progress percentage.</summary>
    public const string ProgressSet = "progress_set";

    // Requirements (subject: the requirement; project_id is set)
    public const string RequirementAdded = "requirement_added";
    public const string RequirementUpdated = "requirement_updated";
    public const string RequirementDone = "requirement_done";
    public const string RequirementReopened = "requirement_reopened";
    public const string RequirementDeleted = "requirement_deleted";

    // Expenses (subject: the expense; project_id is set; the amount in the amount column)
    public const string ExpenseAdded = "expense_added";
    public const string ExpenseRemoved = "expense_removed";

    // Work entries (subject: the work entry; project_id is set; minutes in the payload)
    public const string WorkEntryLogged = "work_entry_logged";
    public const string WorkEntryUpdated = "work_entry_updated";
    public const string WorkEntryDeleted = "work_entry_deleted";
    public const string WorkEntryRestored = "work_entry_restored";

    /// <summary>Subject: the work entry; the comment id in the payload.</summary>
    public const string WorkEntryCommented = "work_entry_commented";

    // Meetings (subject: the meeting; project_id is set; topic and times in the payload)
    public const string MeetingCreated = "meeting_created";
    public const string MeetingUpdated = "meeting_updated";
    public const string MeetingDeleted = "meeting_deleted";

    // Billing
    public const string BillingSettingsUpdated = "billing_settings_updated";

    // Domain settings (subject type only; no subject id)
    public const string DomainSettingsUpdated = "domain_settings_updated";

    /// <summary>Company-wide (no domain); the rate in the amount column.</summary>
    public const string ExchangeRateSet = "exchange_rate_set";

    // Invoices (subject: the invoice; project_id is set; the total in the amount column)
    public const string InvoiceCreated = "invoice_created";
    public const string InvoiceUpdated = "invoice_updated";
    public const string InvoiceDiscarded = "invoice_discarded";
    public const string InvoiceIssued = "invoice_issued";
    public const string InvoiceSent = "invoice_sent";
    public const string InvoiceVoided = "invoice_voided";

    // Payments (subject: the payment; project_id is set; the USD amount in the amount column)
    public const string PaymentRecorded = "payment_recorded";
    public const string PaymentApplied = "payment_applied";
    public const string PaymentVoided = "payment_voided";

    // Company layer (no domain, except schedules, which carry the person's domain)
    public const string CompanySettingsUpdated = "company_settings_updated";
    public const string WorkingTimeChanged = "working_time_changed";
    public const string ScheduleChanged = "schedule_changed";

    // Company overhead (subject: the overhead line; money in the amount column)
    public const string OverheadAdded = "overhead_added";
    public const string OverheadUpdated = "overhead_updated";
    public const string OverheadAmountChanged = "overhead_amount_changed";
    public const string OverheadCancelled = "overhead_cancelled";
    public const string OverheadResumed = "overhead_resumed";
    public const string OverheadRemoved = "overhead_removed";
}
