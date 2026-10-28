namespace ID_Indetails.api.Constants;

/// <summary>Values of <c>notifications.kind</c> written by the API.</summary>
public static class NotificationKinds
{
    /// <summary>Someone was assigned a requirement (subject: the requirement).</summary>
    public const string RequirementAssigned = "requirement_assigned";

    /// <summary>Someone logged work (subject: the work entry). Sent to the domain's admins per its <c>notify_entry</c> setting.</summary>
    public const string WorkLogged = "work_logged";

    /// <summary>One rolling notification per admin per day summing the work logged (digest key <c>work-digest-YYYY-MM-DD</c>).</summary>
    public const string WorkDigest = "work_digest";

    /// <summary>Someone commented on your work entry (subject: the work entry).</summary>
    public const string EntryComment = "entry_comment";

    /// <summary>Someone who doesn't manage meetings set one (subject: the meeting). Sent to the domain's meeting managers.</summary>
    public const string MeetingScheduled = "meeting_scheduled";

    /// <summary>Someone recorded materials on a site (subject: the project; the count in the payload).</summary>
    public const string MaterialsAdded = "materials_added";

    /// <summary>Someone submitted or updated a labour week (subject: the project; the week and whether it was an update in the payload).</summary>
    public const string LabourWeekSubmitted = "labour_week_submitted";

    /// <summary>A project was added (subject: the project). Sent to the domain's people while <c>notify_project</c> is on.</summary>
    public const string ProjectCreated = "project_created";

    /// <summary>A project was put on hold, resumed, completed or reopened (<c>change</c> in the payload). Same recipients as <see cref="ProjectCreated"/>.</summary>
    public const string ProjectStatusChanged = "project_status_changed";

    /// <summary>You were made a project's owner (subject: the project).</summary>
    public const string ProjectOwnerSet = "project_owner_set";

    /// <summary>An active project's progress reached 90% (subject: the project). Sent to its owner and whoever changes project status.</summary>
    public const string ProjectNearlyDone = "project_nearly_done";

    /// <summary>
    /// Requirements were added to a project today: one rolling notification per person per project per day
    /// (digest key <c>requirements-added-{projectId}-YYYY-MM-DD</c>). Sent to the domain's people other than the assignees,
    /// while <c>notify_project</c> is on.
    /// </summary>
    public const string RequirementsAdded = "requirements_added";

    /// <summary>A requirement was ticked (subject: the requirement). Sent to its assignees and whoever manages requirements.</summary>
    public const string RequirementDone = "requirement_done";

    /// <summary>Someone you invited, or someone joining a domain whose invitations you manage, signed up (subject: them).</summary>
    public const string InvitationAccepted = "invitation_accepted";

    /// <summary>Your role changed, by hand or automatically after a permission change (subject: you).</summary>
    public const string RoleChanged = "role_changed";

    /// <summary>You were moved to the other domain (subject: you).</summary>
    public const string DomainChanged = "domain_changed";

    /// <summary>You were put in another team, or taken out of yours (subject: you).</summary>
    public const string TeamChanged = "team_changed";

    /// <summary>Your pay changed from a date (subject: you). Never the amount.</summary>
    public const string PayChanged = "pay_changed";

    /// <summary>Permissions were granted to you, taken away or reset to your role's (subject: you).</summary>
    public const string PermissionsChanged = "permissions_changed";

    /// <summary>Hours were excused for you in the hours ledger (subject: the excuse).</summary>
    public const string HoursExcused = "hours_excused";

    /// <summary>Your working schedule changed from a date (subject: you).</summary>
    public const string ScheduleChanged = "schedule_changed";

    /// <summary>A client was added (subject: the client). Sent to whoever manages the domain's clients.</summary>
    public const string ClientAdded = "client_added";

    /// <summary>An invoice was issued (subject: the invoice). Sent to whoever sees the domain's money.</summary>
    public const string InvoiceIssued = "invoice_issued";

    /// <summary>A scheduled invoice went out on its day (subject: the invoice; <c>to</c> is null when what was on account paid it, so it wasn't emailed). Sent to whoever scheduled it.</summary>
    public const string InvoiceSentAutomatically = "invoice_sent_automatically";

    /// <summary>A scheduled invoice with no address to email reached its day (subject: the invoice). Once, to whoever scheduled it, to send it themselves.</summary>
    public const string InvoiceSendDue = "invoice_send_due";

    /// <summary>A scheduled invoice couldn't be sent on its day and is tried again every hour (subject: the invoice). Once, to whoever scheduled it.</summary>
    public const string InvoiceSendFailed = "invoice_send_failed";

    /// <summary>An overdue invoice is due a reminder, but its client has no email address (subject: the invoice). Once, to whoever may send the domain's invoices.</summary>
    public const string InvoiceReminderDue = "invoice_reminder_due";

    /// <summary>A client payment was recorded (subject: the payment). Same recipients as <see cref="InvoiceIssued"/>.</summary>
    public const string PaymentRecorded = "payment_recorded";

    /// <summary>An Execution site asks the pocket money for money: for whoever sees Execution's money to approve or deny.</summary>
    public const string PocketRequested = "pocket_requested";

    /// <summary>A client project was made by someone who can't send quotes: for those who can, to send one or not.</summary>
    public const string QuoteDue = "quote_due";

    /// <summary>
    /// A change was refused because it would take a client project's profit to zero or below (subject: the blocked
    /// change). To whoever sees the domain's money, to force it through or dismiss it.
    /// </summary>
    public const string ChangeBlocked = "change_blocked";

    /// <summary>A client project's profit is down to 10% of its agreed amount or less (subject: the project). Once per project, to whoever sees the domain's money.</summary>
    public const string ProfitLow = "profit_low";

    /// <summary>A client project's profit reached zero or below, by a forced change or a change nobody made (subject: the project). Once per project, to whoever sees the domain's money.</summary>
    public const string ProfitAtZero = "profit_at_zero";

    /// <summary>A client statement was sent (subject: the statement). Same recipients as <see cref="InvoiceIssued"/>.</summary>
    public const string StatementSent = "statement_sent";

    /// <summary>A client project was completed or archived and its margin is final (subject: the project). Sent to whoever sees the domain's money, with the margin as a percentage and never an amount.</summary>
    public const string ProjectClosed = "project_closed";

    // Scheduled: sent by the notification scheduler, not by anyone's action, so they have no actor.

    /// <summary>What you logged last week (digest key <c>weekly-summary-YYYY-MM-DD</c>, the week's first day). Design, while <c>weekly_summary</c> is on.</summary>
    public const string WeeklySummary = "weekly_summary";

    /// <summary>An active Design project has had no work for 7 days (subject: the project). Once per quiet stretch, while <c>notify_idle</c> is on.</summary>
    public const string ProjectIdle = "project_idle";

    /// <summary>Your open requirement is due tomorrow (subject: the requirement).</summary>
    public const string RequirementDueSoon = "requirement_due_soon";

    /// <summary>An open requirement passed its deadline (subject: the requirement). Sent to its assignees and whoever manages requirements.</summary>
    public const string RequirementOverdue = "requirement_overdue";

    /// <summary>An issued invoice passed its due date unpaid (subject: the invoice). Sent to whoever sees the domain's money.</summary>
    public const string InvoiceOverdue = "invoice_overdue";

    /// <summary>Design employees finished a closed month short of their hours (digest key <c>hours-short-YYYY-MM</c>). Sent to whoever sees salaries.</summary>
    public const string HoursShort = "hours_short";

    /// <summary>
    /// An active client project's cost passed 85% of its payment (subject: the project). No longer sent: <see cref="ProfitLow"/>
    /// took its place; kept so the ones sent before still read.
    /// </summary>
    public const string ProjectCostHigh = "project_cost_high";

    /// <summary>A draft invoice is still unissued 3 days after it was created (subject: the invoice). Once per draft, to whoever sees the domain's money.</summary>
    public const string InvoiceDraftWaiting = "invoice_draft_waiting";

    /// <summary>A client wrote in a project's client portal chat (digest key <c>portal_chat:{projectId}</c>). To the staff taking part.</summary>
    public const string PortalClientMessage = "portal_client_message";

    /// <summary>A client commented on a file (subject: the item; digest key <c>portal_comments:{itemId}</c>). To the project's owner and the file's uploader.</summary>
    public const string PortalClientComment = "portal_client_comment";

    /// <summary>Someone mentioned you in a client portal chat message or comment (subject type <c>message</c> or <c>comment</c>; once each).</summary>
    public const string PortalMention = "portal_mention";

    /// <summary>
    /// Someone commented on a project file (subject: the item; digest key <c>file_comments:{itemId}</c>). To the file's
    /// uploader and its earlier commenters; never the text.
    /// </summary>
    public const string ProjectFileComment = "project_file_comment";
}
