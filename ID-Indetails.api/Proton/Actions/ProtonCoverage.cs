namespace ID_Indetails.api.Proton.Actions;

/// <summary>How Proton handles one of the API's write endpoints (see docs/proton-stage-5-plan.md, section 2.1).</summary>
public enum ProtonCoverageKind
{
    /// <summary>A confirmed action exists for it today.</summary>
    Action,
    /// <summary>To be built as a confirmed action at <see cref="ProtonCoverageEntry.Tier"/>, in its family: Proton can't do it yet.</summary>
    Planned,
    /// <summary>Proton fills the real form; the user presses the app's own button.</summary>
    Prefill,
    /// <summary>Proton opens the place in the app and fills nothing; the user does it.</summary>
    Open,
    /// <summary>A POST that only reads (a check or a preview).</summary>
    ReadOnly,
    /// <summary>
    /// Not something the person signed in here does: someone else's sign-in or invitation, the client's side of the
    /// portal, the phone app's own device, a website visitor. There is nothing for Proton to do it as.
    /// </summary>
    Elsewhere,
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
    /// <summary>The company's public website, which anyone on the internet sees.</summary>
    Website,
}

/// <param name="Endpoint">"Controller.Action", as <see cref="Tools.ProtonEndpoint"/> names it.</param>
/// <param name="Tool">The action tool, for <see cref="ProtonCoverageKind.Action"/>.</param>
/// <param name="Note">The form, the place opened, what reads it, or whose action it is.</param>
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
    private static ProtonCoverageEntry Action(
        string endpoint, string tool, ProtonTier tier = ProtonTier.Standard, ProtonFamily family = ProtonFamily.Core, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Action, tier, family, tool, ExternalSend: externalSend);

    private static ProtonCoverageEntry Planned(string endpoint, ProtonTier tier, ProtonFamily family, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Planned, tier, family, ExternalSend: externalSend);

    private static ProtonCoverageEntry Open(string endpoint, string why, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Open, Note: why, ExternalSend: externalSend);

    private static ProtonCoverageEntry ReadOnly(string endpoint, string usedBy) => new(endpoint, ProtonCoverageKind.ReadOnly, Note: usedBy);

    private static ProtonCoverageEntry Elsewhere(string endpoint, string whose, bool externalSend = false)
        => new(endpoint, ProtonCoverageKind.Elsewhere, Note: whose, ExternalSend: externalSend);

    private static ProtonCoverageEntry Own(string endpoint) => new(endpoint, ProtonCoverageKind.Own);

    /// <summary>
    /// Whether an action may only be proposed on a turn the user typed (decision 33): every family that sends,
    /// deletes, moves money or changes access, company rules or pay rules. A tool without an entry counts as one, so
    /// a new action is held back until it is classified.
    /// </summary>
    public static bool NeedsTypedTurn(string tool)
        => FamilyOf(tool) is not (ProtonFamily.Core or ProtonFamily.Lifecycle);

    /// <summary>Whether a built action emails someone outside the company. Such a tool does nothing else, so it counts whole.</summary>
    public static bool SendsOutside(string tool) => Entries.Any(e => e.Tool == tool && e.ExternalSend);

    /// <summary>Every built action that emails someone outside the company: their daily count is capped together.</summary>
    public static IReadOnlyList<string> SendingTools => Entries.Where(e => e.ExternalSend && e.Tool is not null).Select(e => e.Tool!).Distinct().ToList();

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
        Action("Projects.UpdatePayment", "propose_project_money", T2, ProtonFamily.Money),
        Action("Projects.UpdateBilling", "propose_project_billing", T2, ProtonFamily.Money),
        Planned("Projects.UpdateStaff", T1, ProtonFamily.Core),
        Planned("ProjectDepartments.Set", T1, ProtonFamily.Core),
        Planned("ProjectDepartments.SetShares", T2, ProtonFamily.Money),
        Planned("DesignVos.Create", T2, ProtonFamily.Money),
        Planned("DesignVos.Confirm", T2, ProtonFamily.Money),
        Planned("DesignVos.Cancel", T2, ProtonFamily.Money),
        Planned("DesignVos.Email", T2, ProtonFamily.Money, externalSend: true),
        Planned("SiteLogs.Create", T1, ProtonFamily.Core),
        Planned("SiteLogs.Update", T1, ProtonFamily.Core),
        Planned("Pocket.Contribute", T2, ProtonFamily.Money),
        Planned("Pocket.Ask", T2, ProtonFamily.Money),
        Planned("Pocket.Approve", T2, ProtonFamily.Money),
        Planned("Pocket.Deny", T2, ProtonFamily.Money),
        Planned("SupervisionWithdrawals.Create", T2, ProtonFamily.Money),
        Planned("SupervisionWithdrawals.Delete", T2, ProtonFamily.Money),
        Planned("AccountLabels.Create", T2, ProtonFamily.CompanyRules),
        Planned("AccountLabels.Rename", T2, ProtonFamily.CompanyRules),
        Planned("AccountLabels.Archive", T2, ProtonFamily.CompanyRules),
        Planned("AccountLabels.Restore", T2, ProtonFamily.CompanyRules),
        Planned("AccountLabels.Order", T2, ProtonFamily.CompanyRules),
        Action("Boqs.Create", "propose_boq", T2, ProtonFamily.Money),
        Action("Boqs.Save", "propose_edit_boq", T2, ProtonFamily.Money),
        Action("Boqs.CopyLines", "propose_boq", T2, ProtonFamily.Money),
        Action("Boqs.Duplicate", "propose_boq", T2, ProtonFamily.Money),
        Action("Boqs.Delete", "propose_boq", T2, ProtonFamily.Money),
        Action("Boqs.Agree", "propose_boq_agreement", T2, ProtonFamily.Money),
        Action("Boqs.WithdrawAgreement", "propose_boq_agreement", T2, ProtonFamily.Money),
        Action("Boqs.RecordDelivery", "propose_boq_agreement", T2, ProtonFamily.Money),
        Action("Boqs.Email", "propose_send_boq", T2, ProtonFamily.Money, externalSend: true),
        Planned("BlockedChanges.Force", T2, ProtonFamily.Money),
        Planned("BlockedChanges.Dismiss", T2, ProtonFamily.Money),
        Action("Projects.UpdatePricing", "propose_site_pricing", T2, ProtonFamily.Money),
        Action("Requirements.Create", "propose_add_requirement"),
        Action("Requirements.Update", "propose_update_requirement"),
        Action("Requirements.Done", "propose_set_requirement_done"),
        Action("Requirements.Undone", "propose_set_requirement_done"),
        Action("Requirements.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        Action("Meetings.Create", "propose_schedule_meeting"),
        Action("Meetings.Update", "propose_update_meeting"),
        Action("Meetings.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        Action("Meetings.CreateGeneral", "propose_create_general_meeting"),
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
        Action("WorkEntries.Restore", "propose_restore_my_work"),
        Action("WorkEntries.SetScope", "propose_set_work_entry_scope"),
        Action("WorkEntries.AddComment", "propose_comment_work_entry"),
        Action("BoardLayouts.Save", "propose_arrange_board"),
        // a person's own board layout: a preference, not a record
        Action("BoardLayouts.Delete", "propose_arrange_board"),
        Open("Files.Upload", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),

        // Project expenses (Design) and an Execution site's costs
        Action("Expenses.Create", "propose_add_expense", T2, ProtonFamily.Money),
        Action("Expenses.Delete", "propose_delete_expense", T2, ProtonFamily.Money),
        Action("SiteExpenses.Add", "propose_add_site_expenses", T2, ProtonFamily.Money),
        Action("SiteExpenses.Update", "propose_site_expense_step", T2, ProtonFamily.Money),
        Action("SiteExpenses.Delete", "propose_site_expense_step", T2, ProtonFamily.Money),
        Action("Labour.SubmitWeek", "propose_submit_labour_week", T2, ProtonFamily.Money),
        Action("Labour.SetDefaults", "propose_labour_defaults", T2, ProtonFamily.Money),

        // Billing
        Action("Invoices.Create", "propose_create_invoice", T2, ProtonFamily.Money),
        Action("Invoices.Update", "propose_update_invoice", T2, ProtonFamily.Money),
        Action("Invoices.Discard", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.Issue", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.RecordDelivery", "propose_record_invoice_delivery", T2, ProtonFamily.Money),
        Action("Invoices.Unschedule", "propose_invoice_step", T2, ProtonFamily.Money),
        Action("Invoices.Void", "propose_invoice_step", T2, ProtonFamily.Money),
        Planned("Invoices.Email", T2, ProtonFamily.Money, externalSend: true),
        Planned("Invoices.CreatePlan", T2, ProtonFamily.Money, externalSend: true),
        Planned("Invoices.Schedule", T2, ProtonFamily.Money, externalSend: true),
        Action("Payments.Record", "propose_record_payment", T2, ProtonFamily.Money),
        Action("Payments.Apply", "propose_payment_step", T2, ProtonFamily.Money),
        Action("Payments.Void", "propose_payment_step", T2, ProtonFamily.Money),
        Planned("Payments.SetLabel", T2, ProtonFamily.Money),
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
        Open("Company.SetLogo", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Planned("Company.SetProtonSuggestions", T2, ProtonFamily.CompanyRules),
        Planned("Profile.SetProtonSuggestions", T1, ProtonFamily.Core),
        Planned("Company.SetProtonFiles", T2, ProtonFamily.CompanyRules),
        Planned("Company.SetContactEmail", T2, ProtonFamily.CompanyRules),
        Planned("Company.RemoveLogo", T2, ProtonFamily.CompanyRules),
        Action("Company.SetWorkingTime", "propose_working_time", T2, ProtonFamily.PayRules),
        Action("Schedules.Set", "propose_schedule", T2, ProtonFamily.PayRules),
        Action("HourExcuses.Add", "propose_hour_excuse", T2, ProtonFamily.PayRules),
        Planned("HourExcuses.Delete", T2, ProtonFamily.PayRules),
        ReadOnly("MyTime.OvertimePreview", "an employee's own overtime preview"),

        // People and access
        Action("Employees.Update", "propose_update_employee", T2, ProtonFamily.Access),
        Action("Employees.Deactivate", "propose_deactivate_employee", T2, ProtonFamily.Access),
        Action("Employees.Reactivate", "propose_reactivate_employee", T2, ProtonFamily.Access),
        Action("Employees.EndSessions", "propose_end_sessions", T2, ProtonFamily.Access),
        Action("Employees.RestoreSignIn", "propose_end_sessions", T2, ProtonFamily.Access),
        Open("Employees.ChangeEmail", "it takes a password or a sign-in address, which never go through a chat: Proton opens the form", externalSend: true),
        Open("Employees.ChangePay", "pay amounts never reach Proton: it opens the person's pay"),
        Planned("Employees.ChangeRole", T2, ProtonFamily.Access),
        Planned("Employees.Move", T2, ProtonFamily.Access),
        Open("Employees.SetPhoto", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Planned("Employees.RemovePhoto", T2, ProtonFamily.Access),
        Planned("Permissions.Update", T2, ProtonFamily.Access),
        Planned("Invitations.Create", T2, ProtonFamily.Access, externalSend: true),
        Planned("Invitations.Resend", T2, ProtonFamily.Access, externalSend: true),
        Elsewhere("Invitations.Validate", "the invited person's own sign-up, with their secret token: not something the person signed in here does"),
        Elsewhere("Invitations.Accept", "the invited person's own sign-up, with their secret token: not something the person signed in here does"),
        Action("Teams.Create", "propose_create_team"),
        Action("Teams.Rename", "propose_rename_team"),
        Action("Teams.Delete", "propose_delete_team", T2, ProtonFamily.Deletes),
        Action("Profile.UpdateProfile", "propose_update_my_profile"),
        Open("Profile.SetPhoto", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Planned("Profile.RemovePhoto", T2, ProtonFamily.Deletes),

        // Messages and notifications
        Action("Messages.SendDirect", "propose_send_message", T2, ProtonFamily.Messaging),
        Action("Messages.SendToTeam", "propose_send_message", T2, ProtonFamily.Messaging),
        Action("Messages.MarkDirectRead", "propose_mark_chat_read"),
        Action("Messages.MarkTeamRead", "propose_mark_chat_read"),
        Action("Messages.MarkAnnouncementsRead", "propose_mark_chat_read"),
        Action("Messages.SendAnnouncement", "propose_send_announcement", T2, ProtonFamily.Messaging),
        Action("Notifications.MarkRead", "propose_mark_notifications_read"),
        Action("Notifications.MarkAllRead", "propose_mark_notifications_read"),
        Action("BugReports.Report", "propose_report_bug", T2, ProtonFamily.Messaging, externalSend: true),

        // Sign-in and setup: passwords and sign-in addresses are typed on their own forms, never in a chat
        Elsewhere("Auth.Login", "signing in happens before there is a session for Proton to run in"),
        Open("Auth.Logout", "signing out ends the session Proton runs in: it is the menu's Sign out"),
        Elsewhere("Auth.Refresh", "the browser rotating its own sign-in cookie: not something the person signed in here does"),
        Elsewhere("Auth.ForgotPassword", "an account recovery, started from the sign-in page when signed out"),
        Elsewhere("Auth.ResetPassword", "takes the emailed secret token and a new password, on the sign-in pages"),
        Open("Auth.ChangePassword", "it takes a password or a sign-in address, which never go through a chat: Proton opens the form"),
        Open("Auth.ChangeEmail", "it takes a password or a sign-in address, which never go through a chat: Proton opens the form"),
        Elsewhere("Setup.CreateSuperAdmin", "the first super admin, made with the setup key before anyone can sign in"),
        // the phone app: its sign-in, its device for pushes, and the person's push settings
        Elsewhere("MobileAuth.Login", "the phone app's own sign-in"),
        Elsewhere("MobileAuth.Refresh", "the phone app rotating its own sign-in token"),
        Elsewhere("MobileAuth.Logout", "the phone app ending its own session"),
        Open("MobileAuth.ChangePassword", "it takes a password or a sign-in address, which never go through a chat: Proton opens the form"),
        Elsewhere("Devices.Register", "the phone app registering itself for pushes"),
        Elsewhere("Devices.DeleteCurrent", "the phone app stopping its own pushes"),
        Elsewhere("WebPush.Register", "the browser registering itself for pushes, from the notification settings' switch"),
        Elsewhere("WebPush.Delete", "the browser stopping its own pushes, from the same switch"),
        Planned("Devices.Delete", T2, ProtonFamily.Access),
        Planned("NotificationSettings.Save", T1, ProtonFamily.Core),
        // the client portal: who outside the company may see a project, and the clients' own side
        Action("ProjectPortal.Invite", "propose_portal_invitation", T2, ProtonFamily.Access, externalSend: true),
        Action("ProjectPortal.Resend", "propose_portal_invitation", T2, ProtonFamily.Access, externalSend: true),
        Action("ProjectPortal.RevokeInvitation", "propose_portal_end_access", T2, ProtonFamily.Access),
        Action("ProjectPortal.RevokeAccess", "propose_portal_end_access", T2, ProtonFamily.Access),
        Elsewhere("PortalAuth.Login", "a client's own sign-in to the portal"),
        Elsewhere("PortalAuth.Refresh", "a client's browser rotating its sign-in cookie"),
        Elsewhere("PortalAuth.Logout", "a client ending their portal session"),
        Elsewhere("PortalAuth.ForgotPassword", "a client's own account recovery"),
        Elsewhere("PortalAuth.ResetPassword", "a client's new password, with their emailed token"),
        Elsewhere("PortalInvitations.Validate", "a client opening their own invitation"),
        Elsewhere("PortalInvitations.Accept", "a client accepting their own invitation, with a new password"),
        // a project's files: uploads come from the person's device; arranging them and their comments are actions
        Open("ProjectFiles.StartUpload", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Open("ProjectFiles.PartLinks", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Open("ProjectFiles.Complete", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Open("ProjectFiles.Cancel", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Action("ProjectFiles.Update", "propose_rename_file"),
        Action("ProjectFiles.Delete", "propose_delete", T2, ProtonFamily.Deletes),
        Action("ProjectFiles.Restore", "propose_restore_file"),
        Action("ProjectFiles.Reorder", "propose_reorder_files"),
        Action("ProjectFiles.SetForClient", "propose_file_for_client"),
        Action("ProjectFiles.Comment", "propose_comment_on_file"),
        Action("ProjectFiles.EditComment", "propose_edit_file_comment"),
        Action("ProjectFiles.DeleteComment", "propose_delete_file_comment", T2, ProtonFamily.Deletes),
        Action("ProjectFiles.SetFixed", "propose_set_comment_fixed"),

        // The company's public website: everything there is public the moment it is saved
        Action("Website.CreateProject", "propose_website_project", T2, ProtonFamily.Website),
        Action("Website.UpdateProject", "propose_website_project", T2, ProtonFamily.Website),
        Action("Website.DeleteProject", "propose_website_project", T2, ProtonFamily.Website),
        Action("Website.SetConveyor", "propose_website_conveyor", T2, ProtonFamily.Website),
        Open("Website.AddImage", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Action("Website.UpdateImage", "propose_website_picture", T2, ProtonFamily.Website),
        Action("Website.RemoveImage", "propose_website_picture", T2, ProtonFamily.Website),
        Action("Website.OrderImages", "propose_website_picture", T2, ProtonFamily.Website),
        Action("Website.AddService", "propose_website_service", T2, ProtonFamily.Website),
        Action("Website.UpdateService", "propose_website_service", T2, ProtonFamily.Website),
        Action("Website.DeleteService", "propose_website_service", T2, ProtonFamily.Website),
        Action("Website.OrderServices", "propose_website_order", T2, ProtonFamily.Website),
        Action("Website.AddLocation", "propose_website_location", T2, ProtonFamily.Website),
        Action("Website.UpdateLocation", "propose_website_location", T2, ProtonFamily.Website),
        Action("Website.DeleteLocation", "propose_website_location", T2, ProtonFamily.Website),
        Action("Website.OrderLocations", "propose_website_order", T2, ProtonFamily.Website),
        Action("Website.AddSocialLink", "propose_website_social_link", T2, ProtonFamily.Website),
        Action("Website.UpdateSocialLink", "propose_website_social_link", T2, ProtonFamily.Website),
        Action("Website.DeleteSocialLink", "propose_website_social_link", T2, ProtonFamily.Website),
        Action("Website.OrderSocialLinks", "propose_website_order", T2, ProtonFamily.Website),
        Open("Website.SetFavicon", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Open("Website.CheckWatermark", "it needs a file from the person's own device, which Proton doesn't have: it opens the place to upload it"),
        Action("Website.RemoveFavicon", "propose_remove_website_favicon", T2, ProtonFamily.Website),
        Action("Website.SetUnderConstruction", "propose_website_under_construction", T2, ProtonFamily.Website),
        Elsewhere("PublicWebsite.SendContactMessage", "a website visitor's message, sent without signing in", externalSend: true),
        Action("ProjectPortalContent.AddHero", "propose_portal_heroes", T2, ProtonFamily.Access),
        Action("ProjectPortalContent.RemoveHero", "propose_portal_heroes", T2, ProtonFamily.Access),
        Action("ProjectPortalContent.OrderHeroes", "propose_portal_heroes", T2, ProtonFamily.Access),
        Action("ProjectPortalContent.SetSummary", "propose_portal_summary", T2, ProtonFamily.Access),
        Action("ProjectPortalContent.Publish", "propose_portal_step", T2, ProtonFamily.Access),
        Action("ProjectPortalContent.Discard", "propose_portal_step", T2, ProtonFamily.Access),
        Action("ProjectPortalDiscussion.Send", "propose_discussion_post", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.EditMessage", "propose_discussion_change", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.DeleteMessage", "propose_discussion_change", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.Comment", "propose_discussion_post", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.EditComment", "propose_discussion_change", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.DeleteComment", "propose_discussion_change", T2, ProtonFamily.Messaging),
        Action("ProjectPortalDiscussion.Read", "propose_mark_discussion_read"),
        Elsewhere("PortalDiscussion.Send", "a client's own message"),
        Elsewhere("PortalDiscussion.EditMessage", "a client's own message"),
        Elsewhere("PortalDiscussion.DeleteMessage", "a client's own message"),
        Elsewhere("PortalDiscussion.Comment", "a client's own comment"),
        Elsewhere("PortalDiscussion.EditComment", "a client's own comment"),
        Elsewhere("PortalDiscussion.DeleteComment", "a client's own comment"),
        Elsewhere("PortalDiscussion.Read", "a client's own read marker"),
        Elsewhere("PortalNotifications.Read", "a client's own notifications"),
        Elsewhere("LocalContentStore.PutPart", "the development store's signed link a browser sends a file's bytes to"),

        // Proton itself
        Own("Proton.Ask"),
        Own("Proton.SuggestReply"),
        Own("Proton.Stream"),
        Own("Proton.Confirm"),
        Own("Proton.Cancel"),
        Own("Proton.Remember"),
        Own("Proton.Forget"),
        Own("Proton.DeleteConversation"),
        Own("Proton.RenameConversation"),
        Own("Proton.StartVoice"),
        Own("Proton.EndVoice"),
        Planned("Proton.TopUp", T2, ProtonFamily.Money),
    ];
}
