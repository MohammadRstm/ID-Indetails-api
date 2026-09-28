namespace ID_Indetails.api.Constants;

/// <summary>Machine-readable error codes returned in <c>ApiErrorResponse.Code</c>.</summary>
public static class ErrorCodes
{
    // Generic
    public const string ValidationFailed = "validation_failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";

    // Authentication
    public const string InvalidCredentials = "invalid_credentials";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string RefreshTokenReused = "refresh_token_reused";
    public const string PasswordChangeRequired = "password_change_required";
    public const string CurrentPasswordIncorrect = "current_password_incorrect";

    // Accounts and invitations
    public const string EmailAlreadyRegistered = "email_already_registered";
    public const string InvitationInvalid = "invitation_invalid";
    public const string InvitationNotAllowed = "invitation_not_allowed";
    public const string TeamNotInDomain = "team_not_in_domain";
    public const string UserNotInvited = "user_not_invited";
    public const string UserDeactivated = "user_deactivated";

    // Permissions
    public const string PermissionsLocked = "permissions_locked";
    public const string PermissionChangeNotAllowed = "permission_change_not_allowed";
    public const string UnknownPermission = "unknown_permission";

    // People
    public const string EmployeeChangeNotAllowed = "employee_change_not_allowed";
    public const string LastSuperAdmin = "last_super_admin";
    public const string UserAlreadyDeactivated = "user_already_deactivated";
    public const string UserNotDeactivated = "user_not_deactivated";
    public const string RoleUnchanged = "role_unchanged";
    public const string DomainUnchanged = "domain_unchanged";
    public const string SuperAdminHasNoDomain = "super_admin_has_no_domain";
    public const string PayDateInFuture = "pay_date_in_future";
    public const string PayChangeNotAfterLatest = "pay_change_not_after_latest";

    // Teams
    public const string TeamNameTaken = "team_name_taken";
    public const string TeamNotEmpty = "team_not_empty";

    // Clients
    public const string ContactTaken = "contact_taken";
    public const string ClientHasProjects = "client_has_projects";
    public const string ClientAlreadyArchived = "client_already_archived";
    public const string ClientNotArchived = "client_not_archived";

    // Projects
    public const string ClientIsArchived = "client_archived";
    public const string OwnerNotAllowed = "owner_not_allowed";
    public const string ProjectLocked = "project_locked";
    public const string ProjectHasBilling = "project_has_billing";
    public const string NotForThisDomain = "not_for_this_domain";
    public const string StatusUnchanged = "status_unchanged";
    public const string InvalidStatusChange = "invalid_status_change";

    // Requirements
    public const string ProjectClosed = "project_closed";
    public const string AssigneeNotAllowed = "assignee_not_allowed";

    // Billing
    public const string BillingSettingsMissing = "billing_settings_missing";
    public const string InvoicePrefixTaken = "invoice_prefix_taken";
    public const string ProjectNotBillable = "project_not_billable";
    public const string InvoiceNotDraft = "invoice_not_draft";
    public const string InvoiceNotIssued = "invoice_not_issued";
    public const string InvoiceVoided = "invoice_voided";
    public const string InvoiceHasPayments = "invoice_has_payments";
    public const string IssueDateInFuture = "issue_date_in_future";
    public const string PaymentExceedsBalance = "payment_exceeds_balance";
    public const string PaymentTooSmall = "payment_too_small";
    public const string ExchangeRateMissing = "exchange_rate_missing";
    public const string ReceivedDateInFuture = "received_date_in_future";
    public const string InitialPaymentExists = "initial_payment_exists";
    public const string PaymentAlreadyVoided = "payment_already_voided";
    public const string PaymentAlreadyApplied = "payment_already_applied";
    public const string RateDateInFuture = "rate_date_in_future";
    public const string RateNotAfterLatest = "rate_not_after_latest";

    // Execution site costs
    public const string MaterialStated = "material_stated";
    public const string LabourWeekOutOfRange = "labour_week_out_of_range";
    public const string LabourDayOutsideWeek = "labour_day_outside_week";
    public const string LabourDayInFuture = "labour_day_in_future";
    public const string StatementEmpty = "statement_empty";

    // Dependencies
    public const string EmailDeliveryFailed = "email_delivery_failed";

    // Company layer
    public const string WorkingTimeInPast = "working_time_in_past";
    public const string WorkingTimeAlreadyChanged = "working_time_already_changed";
    public const string OverheadMonthInPast = "overhead_month_in_past";
    public const string OverheadEnded = "overhead_ended";
    public const string OverheadAlreadyCancelled = "overhead_already_cancelled";
    public const string OverheadNotCancelled = "overhead_not_cancelled";

    // Payroll
    public const string SalaryMonthInFuture = "salary_month_in_future";
    public const string ExcuseMonthInFuture = "excuse_month_in_future";
    public const string ProjectPayPeriodInFuture = "project_pay_period_in_future";
}
