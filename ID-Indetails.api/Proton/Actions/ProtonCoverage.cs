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

    private static ProtonCoverageEntry Never(string endpoint, string reason) => new(endpoint, ProtonCoverageKind.Never, Note: reason);

    private static ProtonCoverageEntry Own(string endpoint) => new(endpoint, ProtonCoverageKind.Own);

    /// <summary>
    /// Whether an action may only be proposed on a turn the user typed (decision 33): every family that sends,
    /// deletes, moves money or changes access, company rules or pay rules. A tool without an entry counts as one, so
    /// a new action is held back until it is classified.
    /// </summary>
    public static bool NeedsTypedTurn(string tool)
        => Entries.FirstOrDefault(e => e.Tool == tool)?.Family is not (ProtonFamily.Core or ProtonFamily.Lifecycle);

    private const ProtonTier T1 = ProtonTier.Standard;
    private const ProtonTier T2 = ProtonTier.Strong;

    public static readonly IReadOnlyList<ProtonCoverageEntry> Entries =
    [
        // Projects and their work
        Action("Projects.Create", "propose_create_project"),
        Action("Projects.Update", "propose_update_project"),
        Action("Projects.Hold", "propose_change_project_status"),
        Action("Projects.Resume", "propose_change_project_status"),
        Planned("Projects.Complete", T2, ProtonFamily.Lifecycle),
        Planned("Projects.Archive", T2, ProtonFamily.Lifecycle),
        Planned("Projects.Reopen", T2, ProtonFamily.Lifecycle),
        Planned("Projects.UpdateBilling", T2, ProtonFamily.Money),
        Planned("Projects.UpdatePricing", T2, ProtonFamily.Money),
        Action("Requirements.Create", "propose_add_requirement"),
        Action("Requirements.Update", "propose_update_requirement"),
        Action("Requirements.Done", "propose_set_requirement_done"),
        Action("Requirements.Undone", "propose_set_requirement_done"),
        Planned("Requirements.Delete", T2, ProtonFamily.Deletes),
        Planned("Meetings.Create", T1, ProtonFamily.Core),
        Planned("Meetings.Update", T1, ProtonFamily.Core),
        Planned("Meetings.Delete", T2, ProtonFamily.Deletes),
        Planned("Clients.Create", T1, ProtonFamily.Core),
        Planned("Clients.Update", T1, ProtonFamily.Core),
        Planned("Clients.Archive", T1, ProtonFamily.Core),
        Planned("Clients.Restore", T1, ProtonFamily.Core),
        Planned("Clients.Delete", T2, ProtonFamily.Deletes),
        ReadOnly("Clients.Check", "the duplicate warning when proposing a client"),
        // own work: employees only, once they get Proton (decision 26); the delete has a restore, so it stays standard
        Planned("WorkEntries.Create", T1, ProtonFamily.Core),
        Planned("WorkEntries.Update", T1, ProtonFamily.Core),
        Planned("WorkEntries.Delete", T1, ProtonFamily.Core),
        Planned("WorkEntries.Restore", T1, ProtonFamily.Core),
        Planned("WorkEntries.AddComment", T1, ProtonFamily.Core),
        Planned("BoardLayouts.Save", T1, ProtonFamily.Core),
        // resets the person's own board layout to the default: a preference, not a record
        Planned("BoardLayouts.Delete", T1, ProtonFamily.Core),
        Open("Files.Upload", "uploads come from the person's own device"),

        // Execution sites (money: costs and client prices)
        Planned("Expenses.Create", T2, ProtonFamily.Money),
        Planned("Expenses.Delete", T2, ProtonFamily.Money),
        Planned("Materials.Add", T2, ProtonFamily.Money),
        Planned("Materials.Update", T2, ProtonFamily.Money),
        Planned("Materials.Delete", T2, ProtonFamily.Money),
        Planned("Materials.SetPrice", T2, ProtonFamily.Money),
        Planned("Labour.SubmitWeek", T2, ProtonFamily.Money),
        Planned("Labour.SetDefaults", T2, ProtonFamily.Money),
        Planned("Statements.Create", T2, ProtonFamily.Money),
        Planned("Statements.RecordDelivery", T2, ProtonFamily.Money),
        ReadOnly("Statements.Preview", "the card of a statement proposal"),
        Open("Statements.Email", "it emails the client", externalSend: true),

        // Billing
        Planned("Invoices.Create", T2, ProtonFamily.Money),
        Planned("Invoices.Update", T2, ProtonFamily.Money),
        Planned("Invoices.Discard", T2, ProtonFamily.Money),
        Planned("Invoices.Issue", T2, ProtonFamily.Money),
        Planned("Invoices.RecordDelivery", T2, ProtonFamily.Money),
        Planned("Invoices.Unschedule", T2, ProtonFamily.Money),
        Planned("Invoices.Void", T2, ProtonFamily.Money),
        Open("Invoices.Email", "it emails the client", externalSend: true),
        Open("Invoices.CreatePlan", "an automatic invoice plan emails the client on its own", externalSend: true),
        Open("Invoices.Schedule", "a scheduled invoice is emailed automatically", externalSend: true),
        Planned("Payments.Record", T2, ProtonFamily.Money),
        Planned("Payments.Apply", T2, ProtonFamily.Money),
        Planned("Payments.Void", T2, ProtonFamily.Money),
        // bank fields are never shown to Proton nor stored in a proposal
        Planned("BillingSettings.Save", T2, ProtonFamily.Money),
        Planned("BillingSettings.AddExchangeRate", T2, ProtonFamily.Money),

        // Company
        Planned("Overhead.AddGroup", T2, ProtonFamily.Money),
        Planned("Overhead.RenameGroup", T2, ProtonFamily.Money),
        Planned("Overhead.RemoveGroup", T2, ProtonFamily.Money),
        Planned("Overhead.Create", T2, ProtonFamily.Money),
        Planned("Overhead.Update", T2, ProtonFamily.Money),
        Planned("Overhead.ChangeAmount", T2, ProtonFamily.Money),
        Planned("Overhead.ChangeCurrency", T2, ProtonFamily.Money),
        Planned("Overhead.Cancel", T2, ProtonFamily.Money),
        Planned("Overhead.Resume", T2, ProtonFamily.Money),
        Planned("Company.UpdateSettings", T2, ProtonFamily.CompanyRules),
        Planned("DomainSettings.Save", T2, ProtonFamily.CompanyRules),
        Open("Company.SetLogo", "the logo comes from the person's own device"),
        Open("Company.RemoveLogo", "changes the branding everyone sees: done in settings"),
        Planned("Company.SetWorkingTime", T2, ProtonFamily.PayRules),
        Planned("Schedules.Set", T2, ProtonFamily.PayRules),
        Planned("HourExcuses.Add", T2, ProtonFamily.PayRules),
        Open("HourExcuses.Delete", "takes back an excuse someone may already be paid for"),
        ReadOnly("MyPay.OvertimePreview", "an employee's own overtime preview"),

        // People and access
        Planned("Employees.Update", T2, ProtonFamily.Access),
        Planned("Employees.Deactivate", T2, ProtonFamily.Access),
        Planned("Employees.Reactivate", T2, ProtonFamily.Access),
        Planned("Employees.EndSessions", T2, ProtonFamily.Access),
        Planned("Employees.RestoreSignIn", T2, ProtonFamily.Access),
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
        Planned("Teams.Create", T1, ProtonFamily.Core),
        Planned("Teams.Rename", T1, ProtonFamily.Core),
        Planned("Teams.Delete", T2, ProtonFamily.Deletes),
        Planned("Profile.UpdateProfile", T1, ProtonFamily.Core),
        Open("Profile.SetPhoto", "photos come from the person's own device"),
        Open("Profile.RemovePhoto", "a photo change: done on the profile page"),

        // Messages and notifications
        Planned("Messages.SendDirect", T2, ProtonFamily.Messaging),
        Planned("Messages.SendToTeam", T2, ProtonFamily.Messaging),
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

        // Proton itself
        Own("Proton.Ask"),
        Own("Proton.Stream"),
        Own("Proton.Confirm"),
        Own("Proton.Cancel"),
        Own("Proton.EndConversation"),
        Never("Proton.TopUp", "an assistant raising its own spending limit is a conflict of interest"),
    ];
}
