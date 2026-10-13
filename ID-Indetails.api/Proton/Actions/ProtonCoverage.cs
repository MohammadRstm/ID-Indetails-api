namespace ID_Indetails.api.Proton.Actions;

/// <summary>How Proton handles one of the API's write endpoints (see docs/proton-stage-5-plan.md, section 2.1).</summary>
public enum ProtonCoverageKind
{
    /// <summary>A confirmed action exists for it today.</summary>
    Action,
    /// <summary>Planned as a confirmed action at <see cref="ProtonCoverageEntry.Tier"/>, not built yet: Proton can't do it.</summary>
    Planned,
    /// <summary>Proton fills the real form; the user presses the app's own button.</summary>
    Prefill,
    /// <summary>Proton opens the place in the app and fills nothing; the user does it.</summary>
    Open,
    /// <summary>A POST that only reads (a check or a preview).</summary>
    ReadOnly,
    /// <summary>Proton never does it, nor fills it.</summary>
    Never,
    /// <summary>Proton's own controls: asking, confirming, cancelling.</summary>
    Own,
}

/// <summary>How a confirmed action is confirmed.</summary>
public enum ProtonTier
{
    /// <summary>One Confirm on a card with the exact change.</summary>
    Standard,
    /// <summary>A summary of the effect and a second step; a typed phrase when it can't be undone, moves money or reaches people.</summary>
    Strong,
}

/// <summary>Groups of actions switched on together, each only once approved; everything outside Core is off by default.</summary>
public enum ProtonFamily
{
    Core,
    Lifecycle,
    Deletes,
    Access,
    CompanyRules,
    PayRules,
    Money,
    Messaging,
}

/// <param name="Endpoint">"Controller.Action", as <see cref="Tools.ProtonEndpoint"/> names it.</param>
/// <param name="Tool">The action tool, for <see cref="ProtonCoverageKind.Action"/>.</param>
/// <param name="Note">The form, the place opened, what reads it, or why Proton never does it.</param>
/// <param name="ExternalSend">It emails someone outside Proton's reach to undo.</param>
public sealed record ProtonCoverageEntry(
    string Endpoint,
    ProtonCoverageKind Kind,
    ProtonTier? Tier = null,
    ProtonFamily? Family = null,
    string? Tool = null,
    string? Note = null,
    bool ExternalSend = false);

/// <summary>
/// Every write endpoint of the API and how Proton handles it, so "every action a user can do" is a checked list rather
/// than a hope: a test fails when an endpoint is added without an entry, when an action's tool and endpoint disagree,
/// or when an action needing a sensitive permission isn't confirmed strongly in the matching family.
/// </summary>
public static class ProtonCoverage
{
    private static ProtonCoverageEntry Action(string endpoint, string tool, ProtonTier tier = ProtonTier.Standard, ProtonFamily family = ProtonFamily.Core)
        => new(endpoint, ProtonCoverageKind.Action, tier, family, tool);

    private static ProtonCoverageEntry Planned(string endpoint, ProtonTier tier, ProtonFamily family)
        => new(endpoint, ProtonCoverageKind.Planned, tier, family);

    private static ProtonCoverageEntry Prefill(string endpoint, string form, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Prefill, Note: form, ExternalSend: externalSend);

    private static ProtonCoverageEntry Open(string endpoint, string why, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Open, Note: why, ExternalSend: externalSend);

    private static ProtonCoverageEntry ReadOnly(string endpoint, string usedBy) => new(endpoint, ProtonCoverageKind.ReadOnly, Note: usedBy);

    private static ProtonCoverageEntry Never(string endpoint, string reason, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Never, Note: reason, ExternalSend: externalSend);

    private static ProtonCoverageEntry Own(string endpoint) => new(endpoint, ProtonCoverageKind.Own);

    /// <summary>
    /// Whether an action may only be proposed on a turn the user typed (decision 33): every family that sends,
    /// deletes, moves money or changes access, company rules or pay rules. A tool without an entry counts as one, so
    /// a new action is held back until it is classified.
    /// </summary>
    public static bool NeedsTypedTurn(string tool)
        => FamilyOf(tool) is not (ProtonFamily.Core or ProtonFamily.Lifecycle);

    /// <summary>The family a built action belongs to; null for a tool with no entry, which no switch turns on.</summary>
    public static ProtonFamily? FamilyOf(string tool) => Entries.FirstOrDefault(e => e.Tool == tool)?.Family;

    /// <summary>
    /// How strongly a built action is confirmed: the strongest of its endpoints. This, not anything stored with the
    /// proposal, is what the confirmation enforces. A tool with no entry is strong.
    /// </summary>
    public static ProtonTier TierOf(string tool)
        => Entries.Where(e => e.Tool == tool).Select(e => e.Tier ?? ProtonTier.Strong).DefaultIfEmpty(ProtonTier.Strong).Max();

    private const ProtonTier T1 = ProtonTier.Standard;
    private const ProtonTier T2 = ProtonTier.Strong;

    public static readonly IReadOnlyList<ProtonCoverageEntry> Entries =
    [
        // Projects and their work
        Action("Projects.Create", "propose_create_project"),
        Action("Projects.Update", "propose_update_project"),
        Action("Projects.Hold", "propose_change_project_status"),
        Action("Projects.Resume", "propose_change_project_status"),
        Action("Projects.Complete", "propose_project_lifecycle", T2, ProtonFamily.Lifecycle),
        Action("Projects.Archive", "propose_project_lifecycle", T2, ProtonFamily.Lifecycle),
        Action("Projects.Reopen", "propose_project_lifecycle", T2, ProtonFamily.Lifecycle),
        Action("Projects.UpdateBilling", "propose_project_money", T2, ProtonFamily.Money),
        Action("Projects.UpdatePricing", "propose_project_money", T2, ProtonFamily.Money),
        Action("Requirements.Create", "propose_add_requirement"),
        Action("Requirements.Update", "propose_update_requirement"),
        Action("Requirements.Done", "propose_set_requirement_done"),
        Action("Requirements.Undone", "propose_set_requirement_done"),
        Action("Requirements.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        Action("Meetings.Create", "propose_schedule_meeting"),
        Action("Meetings.Update", "propose_update_meeting"),
        Action("Meetings.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        Action("Clients.Create", "propose_create_client"),
        Action("Clients.Update", "propose_update_client"),
        Action("Clients.Archive", "propose_set_client_archived"),
        Action("Clients.Restore", "propose_set_client_archived"),
        Action("Clients.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        ReadOnly("Clients.Check", "the duplicate warning when proposing a client"),
        // own work: employees only, once they get Proton (decision 26); the delete has a restore, so it stays standard
        Action("WorkEntries.Create", "propose_log_work"),
        Action("WorkEntries.Update", "propose_update_my_work"),
        Action("WorkEntries.Delete", "propose_delete_my_work"),
        Open("WorkEntries.Restore", "a deleted entry is restored from My work, where the person sees what they deleted"),
        Action("WorkEntries.AddComment", "propose_comment_work_entry"),
        Open("BoardLayouts.Save", "the person drags their own board's cards into place"),
        // resets the person's own board layout to the default: a preference, not a record
        Open("BoardLayouts.Delete", "the board's own menu puts its cards back in the default order"),
        Open("Files.Upload", "uploads come from the person's own device"),

        // Execution sites (money: costs and client prices)
        Action("Expenses.Create", "propose_add_expense", T2, ProtonFamily.Money),
        Action("Expenses.Delete", "propose_delete_expense", T2, ProtonFamily.Money),
        Action("Materials.Add", "propose_add_materials", T2, ProtonFamily.Money),
        Action("Materials.Update", "propose_material_step", T2, ProtonFamily.Money),
        Action("Materials.Delete", "propose_material_step", T2, ProtonFamily.Money),
        Action("Materials.SetPrice", "propose_material_step", T2, ProtonFamily.Money),
        Action("Labour.SubmitWeek", "propose_submit_labour_week", T2, ProtonFamily.Money),
        Action("Labour.SetDefaults", "propose_labour_defaults", T2, ProtonFamily.Money),
        Action("Statements.Create", "propose_create_statement", T2, ProtonFamily.Money),
        Action("Statements.RecordDelivery", "propose_record_statement_delivery", T2, ProtonFamily.Money),
        ReadOnly("Statements.Preview", "the card of a statement proposal"),
        Open("Statements.Email", "it emails the client", externalSend: true),

        // Billing
        Action("Invoices.Create", "propose_create_invoice", T2, ProtonFamily.Money),
        Action("Invoices.Update", "propose_update_invoice", T2, ProtonFamily.Money),
        Action("Invoices.Discard", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.Issue", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.RecordDelivery", "propose_record_invoice_delivery", T2, ProtonFamily.Money),
        Action("Invoices.Unschedule", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.Void", "propose_invoice_step", T2, ProtonFamily.Money),
        Open("Invoices.Email", "it emails the client", externalSend: true),
        Open("Invoices.CreatePlan", "an automatic invoice plan emails the client on its own", externalSend: true),
        Open("Invoices.Schedule", "a scheduled invoice is emailed automatically", externalSend: true),
        Action("Payments.Record", "propose_record_payment", T2, ProtonFamily.Money),
        Action("Payments.Apply", "propose_payment_step", T2, ProtonFamily.Money),
        Action("Payments.Void", "propose_payment_step", T2, ProtonFamily.Money),
        // bank fields are never shown to Proton nor stored in a proposal
        Action("BillingSettings.Save", "propose_billing_settings", T2, ProtonFamily.Money),
        Action("BillingSettings.AddExchangeRate", "propose_exchange_rate", T2, ProtonFamily.Money),

        // Company
        Action("Overhead.AddGroup", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.RenameGroup", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.RemoveGroup", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.Create", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.Update", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.ChangeAmount", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.ChangeCurrency", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.Cancel", "propose_overhead", T2, ProtonFamily.Money),
        Action("Overhead.Resume", "propose_overhead", T2, ProtonFamily.Money),
        Action("Company.UpdateSettings", "propose_company_settings", T2, ProtonFamily.CompanyRules),
        Action("DomainSettings.Save", "propose_domain_settings", T2, ProtonFamily.CompanyRules),
        Open("Company.SetLogo", "the logo comes from the person's own device"),
        Open("Company.RemoveLogo", "changes the branding everyone sees: done in settings"),
        Action("Company.SetWorkingTime", "propose_working_time", T2, ProtonFamily.PayRules),
        Action("Schedules.Set", "propose_schedule", T2, ProtonFamily.PayRules),
        Action("HourExcuses.Add", "propose_hour_excuse", T2, ProtonFamily.PayRules),
        Open("HourExcuses.Delete", "takes back an excuse someone may already be paid for"),
        ReadOnly("MyPay.OvertimePreview", "an employee's own overtime preview"),

        // People and access
        Action("Employees.Update", "propose_update_employee", T2, ProtonFamily.Access),
        Action("Employees.Deactivate", "propose_deactivate_employee", T2, ProtonFamily.Access),
        Action("Employees.Reactivate", "propose_reactivate_employee", T2, ProtonFamily.Access),
        Action("Employees.EndSessions", "propose_end_sessions", T2, ProtonFamily.Access),
        Action("Employees.RestoreSignIn", "propose_end_sessions", T2, ProtonFamily.Access),
        Open("Employees.ChangeEmail", "someone's sign-in address is an account-takeover target", externalSend: true),
        Open("Employees.ChangePay", "pay amounts never reach Proton"),
        Open("Employees.ChangeRole", "clears the person's permission overrides"),
        Open("Employees.Move", "clears the person's permission overrides"),
        Open("Employees.SetPhoto", "photos come from the person's own device"),
        Open("Employees.RemovePhoto", "someone else's photo: done on their page"),
        Open("Permissions.Update", "granting access is the first target of a hidden instruction"),
        Prefill("Invitations.Create", "the invite form: address, position and team only", externalSend: true),
        Open("Invitations.Resend", "it emails the person again", externalSend: true),
        Never("Invitations.Validate", "takes the invited person's secret token"),
        Never("Invitations.Accept", "takes the invited person's secret token and new password"),
        Action("Teams.Create", "propose_create_team"),
        Action("Teams.Rename", "propose_rename_team"),
        Action("Teams.Delete", "propose_delete_team", T2, ProtonFamily.Deletes),
        Action("Profile.UpdateProfile", "propose_update_my_profile"),
        Open("Profile.SetPhoto", "photos come from the person's own device"),
        Open("Profile.RemovePhoto", "a photo change: done on the profile page"),

        // Messages and notifications
        Action("Messages.SendDirect", "propose_send_message", T2, ProtonFamily.Messaging),
        Action("Messages.SendToTeam", "propose_send_message", T2, ProtonFamily.Messaging),
        Open("Messages.MarkDirectRead", "opening the chat marks it read; Proton never marks unseen messages read"),
        Open("Messages.MarkTeamRead", "opening the chat marks it read; Proton never marks unseen messages read"),
        Open("Notifications.MarkRead", "opening it marks it read; Proton never marks unseen ones read"),
        Open("Notifications.MarkAllRead", "Proton never marks unseen notifications read"),
        Prefill("BugReports.Report", "the report-a-problem form", externalSend: true),

        // Credentials and setup: never through Proton
        Never("Auth.Login", "takes a password"),
        Never("Auth.Logout", "ends the session Proton itself runs in"),
        Never("Auth.Refresh", "rotates the sign-in cookie"),
        Never("Auth.ForgotPassword", "starts an account recovery"),
        Never("Auth.ResetPassword", "takes a secret token and a new password"),
        Never("Auth.ChangePassword", "takes passwords"),
        Never("Auth.ChangeEmail", "changes the person's own sign-in address"),
        Never("Setup.CreateSuperAdmin", "takes the setup key"),
        // the phone app: its sign-in, its device for pushes, and the person's push settings
        Never("MobileAuth.Login", "takes a password"),
        Never("MobileAuth.Refresh", "rotates the phone app's sign-in token"),
        Never("MobileAuth.Logout", "ends a phone's session"),
        Never("MobileAuth.ChangePassword", "takes passwords"),
        Never("Devices.Register", "the phone app registers its own device for pushes"),
        Never("Devices.DeleteCurrent", "the phone app stops its own pushes"),
        Never("Devices.Delete", "takes a phone off the person's account: done by the person"),
        Never("NotificationSettings.Save", "the person's quiet hours and muted pushes: set by the person"),
        // the client portal: who outside the company may see a project is decided by a person, and clients' own sign-in
        Never("ProjectPortal.Invite", "gives someone outside the company access to a project", externalSend: true),
        Never("ProjectPortal.Resend", "emails a sign-up link to someone outside the company", externalSend: true),
        Never("ProjectPortal.RevokeInvitation", "decides who outside the company may see a project"),
        Never("ProjectPortal.RevokeAccess", "decides who outside the company may see a project"),
        Never("PortalAuth.Login", "a client's sign-in: takes a password"),
        Never("PortalAuth.Refresh", "rotates a client's sign-in cookie"),
        Never("PortalAuth.Logout", "ends a client's session"),
        Never("PortalAuth.ForgotPassword", "starts a client's account recovery"),
        Never("PortalAuth.ResetPassword", "takes a secret token and a new password"),
        Never("PortalInvitations.Validate", "reads a client's secret invitation token"),
        Never("PortalInvitations.Accept", "takes a secret token and a new password"),
        Never("ProjectPortalContent.StartUpload", "files for the client portal are chosen and sent by a person"),
        Never("ProjectPortalContent.PartLinks", "hands out links to write into the client portal's file storage"),
        Never("ProjectPortalContent.Complete", "adds an uploaded file to the client portal"),
        Never("ProjectPortalContent.Cancel", "stops a person's own upload"),
        Never("ProjectPortalContent.Update", "decides what the client portal shows"),
        Never("ProjectPortalContent.Delete", "deletes a client portal file for good"),
        Never("LocalContentStore.PutPart", "the development store's signed link a browser sends a file's bytes to"),

        // Proton itself
        Own("Proton.Ask"),
        Own("Proton.Stream"),
        Own("Proton.Confirm"),
        Own("Proton.Cancel"),
        Own("Proton.Remember"),
        Own("Proton.Forget"),
        Own("Proton.DeleteConversation"),
        Own("Proton.RenameConversation"),
        Own("Proton.StartVoice"),
        Own("Proton.EndVoice"),
        Never("Proton.TopUp", "an assistant raising its own spending limit is a conflict of interest"),
    ];
}
