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

    /// <summary>A client payment was recorded (subject: the payment). Same recipients as <see cref="InvoiceIssued"/>.</summary>
    public const string PaymentRecorded = "payment_recorded";

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

    /// <summary>An active client project's cost passed 85% of its payment (subject: the project). Once per project, to whoever sees the domain's money.</summary>
    public const string ProjectCostHigh = "project_cost_high";

    /// <summary>A draft invoice is still unissued 3 days after it was created (subject: the invoice). Once per draft, to whoever sees the domain's money.</summary>
    public const string InvoiceDraftWaiting = "invoice_draft_waiting";
}
