using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>What Proton may ask the app to do on screen. The app decides how (its own routes) and whether (its own access rules).</summary>
public enum ProtonUiCommandType
{
    OpenProject,
    OpenClient,
    OpenPerson,
    OpenPage,
    /// <summary>Open a form, dialog or drawer, on the page it belongs to.</summary>
    OpenSurface,
    /// <summary>Fill a form already open (or opened just before) for the person to review and save: never submitted.</summary>
    FillForm,
    /// <summary>Change one of the person's own display choices in this browser (theme, message sounds).</summary>
    SetPreference,
    /// <summary>Fetch the current page's data again.</summary>
    Refresh,
}

/// <summary>
/// The pages Proton can open, by meaning rather than address. Salaries, project pay and the Proton budget open for
/// those who may see them, but Proton never reads their figures.
/// </summary>
public enum ProtonPage
{
    Dashboard,
    Projects,
    Clients,
    People,
    Financials,
    Meetings,
    Activity,
    Reports,
    CompanyOverview,
    CompanyExpenses,
    Settings,
    Notifications,
    Messages,
    Help,
    Schedules,
    Salaries,
    ProjectPay,
    ProtonBudget,
    CompanySettings,
    CompanyProfile,
    Launcher,
    MyWork,
    MyProfile,
    // the Website area: the company's public website, for those who may see it
    WebsiteProjects,
    WebsiteAnalytics,
    WebsiteServices,
    WebsiteLocations,
    WebsiteContact,
    WebsiteSite,
    WebsiteStaff,
}

/// <summary>
/// The forms, dialogs and drawers Proton can open, by meaning. One-click confirm dialogs (complete a project,
/// deactivate someone, void, delete, top-up, publish to the client) are deliberately absent: those are proposed on a
/// card instead.
/// </summary>
public enum ProtonSurface
{
    NewProject,
    NewClient,
    InvitePerson,
    EditProject,
    EditClient,
    ProjectReport,
    ProjectPaymentTerms,
    AddProjectExpense,
    SitePricing,
    NewInvoice,
    RecordPayment,
    NewStatement,
    NewMeeting,
    AddCompanyExpense,
    MessagePerson,
    MessageTeam,
    LogWork,
    ReportBug,
    SettingsSection,
    CompanyOverviewTab,
    /// <summary>The website project form, empty, on the Website's projects page.</summary>
    NewWebsiteProject,
    /// <summary>A website project's form (the record: a website project).</summary>
    EditWebsiteProject,
    /// <summary>The comments on one of a project's files, beside its Files tab (the record: the project, with the file's item id).</summary>
    FileComments,
    /// <summary>A project's discussion with its client, on its client portal tab, optionally on one file's thread.</summary>
    PortalDiscussion,
    /// <summary>The project's client portal draft as the client would see it, in a new browser tab.</summary>
    PortalPreview,
    /// <summary>The invite form on the Website's staff page.</summary>
    InviteWebsiteStaff,
    /// <summary>A project file opened to look at, on the project's files tab (its item id in <c>ItemId</c>).</summary>
    ViewProjectFile,
}

public enum ProtonSettingsSection
{
    General,
    Time,
    Finance,
    Projects,
    Notifications,
    Teams,
    Admins,
    Data,
}

public enum ProtonCompanyTab
{
    Overview,
    People,
    Teams,
}

public enum ProtonTheme
{
    Light,
    Dark,
}

/// <summary>
/// A project page's tabs, in the page's order. Design projects: financials (money), overview (the person's own work),
/// analytics and contributors (everyone's work), timeline (the history calendar), files (the project's files) and
/// portal (what its client sees, and the discussion with them); every project has requirements and meetings. The page
/// opens on the first one the person may open.
/// </summary>
public enum ProtonProjectSection
{
    Financials,
    Overview,
    Analytics,
    Contributors,
    Requirements,
    Timeline,
    Meetings,
    Files,
    Portal,
}

/// <summary>
/// A part of a project's files or client portal tab: a section of the files (presentation, model, drawing), and on the
/// portal tab also who has access and the discussion.
/// </summary>
public enum ProtonProjectPart
{
    Presentation,
    Model,
    Drawing,
    Access,
    Discussion,
}

public enum ProtonFinancialsTab
{
    Overview,
    Projects,
    Clients,
    Billing,
    Costs,
}

/// <summary>
/// One semantic instruction for the app: enums and ids, never an address. Built only by Proton's UI tools and actions
/// from records the user may see; the app validates it again and maps it to its own route.
/// </summary>
/// <param name="Label">What is being opened, in words, from the record's own name ("Villa Mounir").</param>
/// <param name="Domain">The business the record or page belongs to; null for company pages and company-wide people.</param>
/// <param name="ClientId">Projects page only: show that client's projects.</param>
/// <param name="Surface">For open_surface: what to open; <paramref name="Id"/> is the record it is for, when it needs one.</param>
/// <param name="Sounds">For set_preference: message sounds on or off.</param>
/// <param name="Part">With the files or portal tab: which part of it.</param>
/// <param name="ItemId">With file_comments or portal_discussion: the project file whose comments open.</param>
public sealed record ProtonUiCommand(
    ProtonUiCommandType Type,
    string Label,
    long? Id = null,
    string? Domain = null,
    ProtonProjectSection? Section = null,
    ProtonPage? Page = null,
    long? ClientId = null,
    ProtonFinancialsTab? Tab = null,
    ProtonSurface? Surface = null,
    ProtonSettingsSection? SettingsSection = null,
    ProtonCompanyTab? CompanyTab = null,
    ProtonTheme? Theme = null,
    bool? Sounds = null,
    ProtonFormFill? Fill = null,
    ProtonProjectPart? Part = null,
    long? ItemId = null)
{
    public static ProtonUiCommand Open(ProtonRef record, ProtonProjectSection? section = null, ProtonProjectPart? part = null) => record.Type switch
    {
        ProtonRefType.Project => new(ProtonUiCommandType.OpenProject, record.Name, record.Id, record.Domain,
            section is ProtonProjectSection.Overview ? null : section, Part: section is ProtonProjectSection.Files or ProtonProjectSection.Portal ? part : null),
        ProtonRefType.Client => new(ProtonUiCommandType.OpenClient, record.Name, record.Id, record.Domain),
        ProtonRefType.Person => new(ProtonUiCommandType.OpenPerson, record.Name, record.Id, record.Domain),
        // a website project opens in its own form, on the Website's projects page
        ProtonRefType.WebsiteProject => new(ProtonUiCommandType.OpenSurface, record.Name, record.Id, record.Domain, Surface: ProtonSurface.EditWebsiteProject),
        // a team has no page of its own: its chat opens through open_surface
        _ => new(ProtonUiCommandType.OpenSurface, record.Name, record.Id, record.Domain, Surface: ProtonSurface.MessageTeam),
    };

    public static ProtonUiCommand Refresh { get; } = new(ProtonUiCommandType.Refresh, "this page");

    /// <summary>Takes the person to another page (opening a surface does too: it lives on its own page).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Navigates => Type is not (ProtonUiCommandType.Refresh or ProtonUiCommandType.SetPreference or ProtonUiCommandType.FillForm);
}

/// <summary>An option the user can pick instead of typing; built from a record a tool returned, never from the model's words.</summary>
/// <param name="Detail">What tells it apart: kind and business ("Project · Design").</param>
public sealed record ProtonChoice(ProtonRefType Type, long Id, string Label, string? Detail, string? Domain);

/// <summary>An answer to Proton's own question that the app can offer as a button: a period, a business, or yes or no.</summary>
public enum ProtonQuickReply
{
    Yes,
    No,
    Design,
    Execution,
    Today,
    ThisWeek,
    LastWeek,
    ThisMonth,
    LastMonth,
    ThisQuarter,
    LastQuarter,
    ThisYear,
    LastYear,
}

/// <summary>A quick-reply button: the value, in the app's own words. The model never writes the label.</summary>
public sealed record ProtonQuickReplyOption(ProtonQuickReply Reply, string Label);

public static class ProtonUiRules
{
    public const int MinChoices = 2;
    public const int MaxChoices = 6;

    public const int MaxSuggestions = 3;
    public const int MaxSuggestionLength = 60;

    public static bool IsYesOrNo(ProtonQuickReply reply) => reply is ProtonQuickReply.Yes or ProtonQuickReply.No;

    public static ProtonQuickReplyOption Option(ProtonQuickReply reply) => new(reply, reply switch
    {
        ProtonQuickReply.ThisWeek => "This week",
        ProtonQuickReply.LastWeek => "Last week",
        ProtonQuickReply.ThisMonth => "This month",
        ProtonQuickReply.LastMonth => "Last month",
        ProtonQuickReply.ThisQuarter => "This quarter",
        ProtonQuickReply.LastQuarter => "Last quarter",
        ProtonQuickReply.ThisYear => "This year",
        ProtonQuickReply.LastYear => "Last year",
        _ => reply.ToString(),
    });

    /// <summary>
    /// What a reply asks the app to do, in the order the app runs it: one place to go (the last asked for, so a later
    /// "open X" replaces an earlier one; a surface brings its own page, so it wins over a plain page), then one fill
    /// (the last), then the preferences asked for (the last of each kind). A refresh only when nothing else moves the page.
    /// </summary>
    public static IReadOnlyList<ProtonUiCommand> Pick(IEnumerable<ProtonUiCommand> commands)
    {
        var all = commands.ToList();
        var sequence = new List<ProtonUiCommand>();
        if ((all.LastOrDefault(c => c.Type == ProtonUiCommandType.OpenSurface) ?? all.LastOrDefault(c => c.Navigates)) is { } go)
        {
            sequence.Add(go);
        }

        if (all.LastOrDefault(c => c.Type == ProtonUiCommandType.FillForm) is { } fill)
        {
            sequence.Add(fill);
        }

        if (all.LastOrDefault(c => c.Theme is not null) is { } theme)
        {
            sequence.Add(theme);
        }

        if (all.LastOrDefault(c => c.Sounds is not null) is { } sounds)
        {
            sequence.Add(sounds);
        }

        if (sequence.Count == 0 && all.Any(c => c.Type == ProtonUiCommandType.Refresh))
        {
            sequence.Add(ProtonUiCommand.Refresh);
        }

        return sequence;
    }
}
