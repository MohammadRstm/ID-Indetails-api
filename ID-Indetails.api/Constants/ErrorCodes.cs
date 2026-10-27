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
    public const string PasswordResetInvalid = "password_reset_invalid";
    public const string SignInSuspended = "sign_in_suspended";

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
    public const string SessionsAlreadyEnded = "sessions_already_ended";
    public const string SessionsNotEnded = "sessions_not_ended";
    public const string UserNotDeactivated = "user_not_deactivated";
    public const string LastWorkingDayInvalid = "last_working_day_invalid";
    public const string PayGapRequired = "pay_gap_required";
    public const string PayGapNotAsked = "pay_gap_not_asked";
    public const string RoleUnchanged = "role_unchanged";
    public const string DomainUnchanged = "domain_unchanged";
    public const string EmailUnchanged = "email_unchanged";
    public const string SuperAdminHasNoDomain = "super_admin_has_no_domain";
    public const string FixedSalaryPaidMonthly = "fixed_salary_paid_monthly";
    public const string FixedSalaryNeedsMonthlyPay = "fixed_salary_needs_monthly_pay";
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

    // Work entries
    public const string ProjectNotAcceptingWork = "project_not_accepting_work";
    public const string EntryInFuture = "entry_in_future";
    public const string EntryOverlaps = "entry_overlaps";
    public const string DescriptionRequired = "description_required";
    public const string EditWindowClosed = "edit_window_closed";
    public const string MonthClosed = "month_closed";

    // Messaging
    public const string NoTeamChat = "no_team_chat";
    public const string TeamHasChat = "team_has_chat";

    // Files
    public const string FileTypeNotAllowed = "file_type_not_allowed";
    public const string FileTooLarge = "file_too_large";
    public const string UploadQuotaReached = "upload_quota_reached";
    public const string FileInUse = "file_in_use";

    // Billing
    public const string BillingSettingsMissing = "billing_settings_missing";
    public const string InvoicePrefixTaken = "invoice_prefix_taken";
    public const string ProjectNotBillable = "project_not_billable";
    public const string InvoiceNotDraft = "invoice_not_draft";
    public const string InvoiceNotIssued = "invoice_not_issued";
    public const string InvoiceVoided = "invoice_voided";
    public const string InvoiceHasPayments = "invoice_has_payments";
    public const string IssueDateInFuture = "issue_date_in_future";
    public const string SendDateInPast = "send_date_in_past";
    public const string PaymentExceedsBalance = "payment_exceeds_balance";
    public const string PaymentTooSmall = "payment_too_small";
    public const string ExchangeRateMissing = "exchange_rate_missing";
    public const string MaterialsNotSent = "materials_not_sent";
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
    public const string LabourWeekChanged = "labour_week_changed";

    /// <summary>403. Only a site's own staff report its labour and write its daily logs.</summary>
    public const string NotOnSiteTeam = "not_on_site_team";

    /// <summary>422. A daily log can only be changed on the day it was written.</summary>
    public const string SiteLogLocked = "site_log_locked";

    /// <summary>422. The Execution box holds less than the money asked for.</summary>
    public const string BoxShort = "box_short";

    /// <summary>422. The box only sends money to a project in the red.</summary>
    public const string BoxProjectNotInRed = "box_project_not_in_red";

    /// <summary>422. The project isn't waiting for a decision on its money in the box.</summary>
    public const string BoxNoDecision = "box_no_decision";
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
    public const string OverheadSameCurrency = "overhead_same_currency";
    public const string OverheadCarriedOn = "overhead_carried_on";
    public const string OverheadGroupNotFound = "overhead_group_not_found";
    public const string OverheadGroupExists = "overhead_group_exists";
    public const string OverheadGroupInUse = "overhead_group_in_use";

    // Payroll
    public const string SalaryMonthInFuture = "salary_month_in_future";
    public const string ExcuseMonthInFuture = "excuse_month_in_future";
    public const string ProjectPayPeriodInFuture = "project_pay_period_in_future";

    // Proton
    public const string ProtonUnavailable = "proton_unavailable";
    public const string ProtonAllowanceUsed = "proton_allowance_used";
    public const string ProtonBudgetUsed = "proton_budget_used";
    public const string ProtonBusy = "proton_busy";
    public const string ProtonConversationEnded = "proton_conversation_ended";
    public const string ProtonActionNotFound = "proton_action_not_found";
    public const string ProtonActionNotPending = "proton_action_not_pending";
    public const string ProtonActionExpired = "proton_action_expired";
    public const string ProtonActionStale = "proton_action_stale";

    /// <summary>A confirmed change hit an unexpected error while it was being made: it may or may not have been saved.</summary>
    public const string ProtonActionUncertain = "proton_action_uncertain";
    public const string ProtonChoiceInvalid = "proton_choice_invalid";
    public const string ProtonActionsOff = "proton_actions_off";
    public const string ProtonTooManyPending = "proton_too_many_pending";
    public const string ProtonActionLimit = "proton_action_limit";
    public const string ProtonPhraseMismatch = "proton_phrase_mismatch";

    /// <summary>Voice is switched off, the provider refused the session, or the session asked about isn't running.</summary>
    public const string ProtonVoiceUnavailable = "proton_voice_unavailable";

    /// <summary>The person already has a voice session open.</summary>
    public const string ProtonVoiceBusy = "proton_voice_busy";

    /// <summary>A spoken question that was already asked in this session: it isn't asked twice.</summary>
    public const string ProtonVoiceRepeated = "proton_voice_repeated";

    /// <summary>Not something Proton may remember: not the person's own words, a full list, or not a preference.</summary>
    public const string ProtonMemoryRefused = "proton_memory_refused";

    // Client portal
    /// <summary>No invitation matches the link.</summary>
    public const string PortalInvitationInvalid = "portal_invitation_invalid";

    public const string PortalInvitationExpired = "portal_invitation_expired";
    public const string PortalInvitationRevoked = "portal_invitation_revoked";

    /// <summary>The link was already used to set up the account: sign in instead.</summary>
    public const string PortalInvitationUsed = "portal_invitation_used";

    /// <summary>The project can't have a client portal: not a Design client project, or archived.</summary>
    public const string PortalProjectNotEligible = "portal_project_not_eligible";

    /// <summary>Only an email address among the project client's contacts can be invited.</summary>
    public const string PortalEmailNotClientContact = "portal_email_not_client_contact";

    /// <summary>The client already has access, or a pending invitation, to this project.</summary>
    public const string PortalAlreadyInvited = "portal_already_invited";

    public const string PortalAccessAlreadyEnded = "portal_access_already_ended";

    /// <summary>503. Client portal content storage isn't configured (ContentStore:Provider), or can't be reached.</summary>
    public const string ContentStorageUnavailable = "content_storage_unavailable";

    /// <summary>The file's extension isn't one the client portal accepts.</summary>
    public const string PortalFileTypeNotAllowed = "portal_file_type_not_allowed";

    /// <summary>The uploaded bytes aren't the kind of file the extension says; the upload was refused and removed.</summary>
    public const string PortalFileContentMismatch = "portal_file_content_mismatch";

    /// <summary>Not every part of the upload has arrived, or a part has the wrong size.</summary>
    public const string PortalUploadIncomplete = "portal_upload_incomplete";

    /// <summary>The upload was cancelled, refused or expired, so it can't continue or be completed.</summary>
    public const string PortalUploadEnded = "portal_upload_ended";

    /// <summary>The project's client portal content can't change now (an archived project is read-only).</summary>
    public const string PortalContentReadOnly = "portal_content_read_only";

    /// <summary>409. The drafts changed since the revision the person saw: they should look again before publishing or discarding.</summary>
    public const string PortalRevisionStale = "portal_revision_stale";

    /// <summary>409. A new order must list exactly the section's current files; someone added, removed or moved one meanwhile.</summary>
    public const string PortalOrderStale = "portal_order_stale";

    /// <summary>Only a JPEG, PNG or WebP image can be a hero image.</summary>
    public const string PortalHeroNotImage = "portal_hero_not_image";

    /// <summary>409. The image is already one of the draft hero images.</summary>
    public const string PortalHeroAlreadyAdded = "portal_hero_already_added";

    /// <summary>422. The draft already has as many hero images as allowed (12).</summary>
    public const string PortalHeroLimit = "portal_hero_limit";

    /// <summary>Discarding needs a published version to go back to.</summary>
    public const string PortalNeverPublished = "portal_never_published";

    /// <summary>422. The project is archived: its discussion can be read, but nothing new can be written.</summary>
    public const string PortalDiscussionClosed = "portal_discussion_closed";

    /// <summary>422. The website project lacks something the public site needs (the message lists what).</summary>
    public const string WebsiteProjectIncomplete = "website_project_incomplete";

    /// <summary>422. The moving strip on the website shows at most 20 projects, all of them published.</summary>
    public const string WebsiteConveyorInvalid = "website_conveyor_invalid";

    /// <summary>422. A website project has at most five concept pictures.</summary>
    public const string WebsiteConceptFull = "website_concept_full";

    /// <summary>409. A new order must list exactly the current items; one was added or removed meanwhile.</summary>
    public const string WebsiteOrderStale = "website_order_stale";

    /// <summary>503. The website's contact form can't send: the company has no contact email yet, and there is no fallback address.</summary>
    public const string WebsiteContactUnavailable = "website_contact_unavailable";

    /// <summary>422. A website place couldn't be found on the map: the message says what to type in.</summary>
    public const string WebsitePlaceNotFound = "website_place_not_found";

    /// <summary>422. A website project only links to a system project once.</summary>
    public const string WebsiteProjectAlreadyLinked = "website_project_already_linked";

    /// <summary>503. Google Analytics isn't connected: it is switched off, or its property or key is missing.</summary>
    public const string WebsiteAnalyticsNotConfigured = "website_analytics_not_configured";

    /// <summary>503. Google Analytics didn't answer, refused the request or is out of quota; the reason is in the server log.</summary>
    public const string WebsiteAnalyticsUnavailable = "website_analytics_unavailable";
}
