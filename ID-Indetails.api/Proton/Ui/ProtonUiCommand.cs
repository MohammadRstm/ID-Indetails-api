using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>What Proton may ask the app to do on screen. The app decides how (its own routes) and whether (its own access rules).</summary>
public enum ProtonUiCommandType
{
    OpenProject,
    OpenClient,
    OpenPerson,
    OpenPage,
    /// <summary>Fetch the current page's data again.</summary>
    Refresh,
}

/// <summary>The pages Proton can open, by meaning rather than address. Settings, pay and salaries are deliberately absent.</summary>
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
}

public enum ProtonProjectSection
{
    Overview,
    Requirements,
    Timeline,
    Meetings,
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
public sealed record ProtonUiCommand(
    ProtonUiCommandType Type,
    string Label,
    long? Id = null,
    string? Domain = null,
    ProtonProjectSection? Section = null,
    ProtonPage? Page = null,
    long? ClientId = null,
    ProtonFinancialsTab? Tab = null)
{
    public static ProtonUiCommand Open(ProtonRef record, ProtonProjectSection? section = null) => record.Type switch
    {
        ProtonRefType.Project => new(ProtonUiCommandType.OpenProject, record.Name, record.Id, record.Domain,
            section is ProtonProjectSection.Overview ? null : section),
        ProtonRefType.Client => new(ProtonUiCommandType.OpenClient, record.Name, record.Id, record.Domain),
        _ => new(ProtonUiCommandType.OpenPerson, record.Name, record.Id, record.Domain),
    };

    public static ProtonUiCommand Refresh { get; } = new(ProtonUiCommandType.Refresh, "this page");

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Navigates => Type != ProtonUiCommandType.Refresh;
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
    /// What a reply asks the app to do: at most one navigation (the last asked for, so a later "open X" replaces an
    /// earlier one) and at most one refresh, which is pointless when the reply navigates anyway.
    /// </summary>
    public static IReadOnlyList<ProtonUiCommand> Pick(IEnumerable<ProtonUiCommand> commands)
    {
        var all = commands.ToList();
        return all.LastOrDefault(c => c.Navigates) is { } navigation ? [navigation]
            : all.Any(c => c.Type == ProtonUiCommandType.Refresh) ? [ProtonUiCommand.Refresh]
            : [];
    }
}
