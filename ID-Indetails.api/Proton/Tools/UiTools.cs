using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Ui;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>What a UI tool tells the model: what the app will open, and that the user sees it once the answer is shown.</summary>
public sealed record UiOutcome(string Opens, string Note);

public sealed record OpenRecordInput
{
    public required ProtonRefType Type { get; init; }

    [Description("The id from find or another tool. Never guess one.")]
    public required long Id { get; init; }

    [Description("Projects only: the tab to open. Default overview.")]
    public ProtonProjectSection? Section { get; init; }
}

/// <summary>
/// Opens a project (optionally on a tab), a client or a person in the app. The record is read first exactly as its own
/// page would read it, so an id the user can't see is "not found" and nothing opens.
/// </summary>
public sealed class OpenRecordTool(ProtonRecords records) : ProtonTool<OpenRecordInput>
{
    private static readonly ProtonEndpoint Requirements = ProtonEndpoint.Of<RequirementsController>(nameof(RequirementsController.List));
    private static readonly ProtonEndpoint Timeline = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.Timeline));
    private static readonly ProtonEndpoint Meetings = ProtonEndpoint.Of<MeetingsController>(nameof(MeetingsController.List));

    public override string Name => "open_record";

    public override string Description =>
        "Open a project, client or person in the app for the user (\"take me to…\", \"open…\", \"show me its requirements\"). " +
        "Use the id from find. Projects can open on a tab: requirements, timeline (Design) or meetings. Opening needs no " +
        "other lookup; only read data too if the user also asked a question about it.";

    public override ProtonEndpoint Endpoint { get; } = ProtonRecords.ProjectGet;

    protected override async Task<ProtonToolOutput> RunAsync(OpenRecordInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var record = await records.FindAsync(input.Type, input.Id, context, cancellationToken)
            ?? throw new Exceptions.Base.NotFoundException("Not found.");
        var section = input.Type == ProtonRefType.Project ? input.Section : null;
        var note = "The app opens it when your answer is shown. Say what you opened in a few words.";
        if (section is { } asked && !await CanOpenAsync(asked, record, context))
        {
            note = $"The {asked.ToString().ToLowerInvariant()} tab isn't available to this user here, so the project opens on its overview. Say so.";
            section = null;
        }

        var command = ProtonUiCommand.Open(record, section);
        return new ProtonToolOutput(new UiOutcome(Describe(command), note), [record]) { UiCommand = command };
    }

    private static async Task<bool> CanOpenAsync(ProtonProjectSection section, ProtonRef project, ProtonToolContext context) => section switch
    {
        ProtonProjectSection.Requirements => await context.CanCallAsync(Requirements),
        ProtonProjectSection.Timeline => project.Domain == DomainCodes.Design && await context.CanCallAsync(Timeline),
        ProtonProjectSection.Meetings => await context.CanCallAsync(Meetings),
        _ => true,
    };

    internal static string Describe(ProtonUiCommand command)
        => command.Section is { } section ? $"{command.Label}: {section.ToString().ToLowerInvariant()}" : command.Label;
}

public sealed record OpenPageInput
{
    public required ProtonPage Page { get; init; }

    [Description("Which business. Leave out for the user's own; company pages have none. People, activity and reports are Design only.")]
    public ProtonDomain? Domain { get; init; }

    [Description("projects page only: show this client's projects (id from find).")]
    public long? ClientId { get; init; }

    [Description("financials page only.")]
    public ProtonFinancialsTab? Tab { get; init; }
}

/// <summary>Opens one of a fixed set of pages, if the user may open it: each page needs what its own endpoint needs.</summary>
public sealed class OpenPageTool(ProtonRecords records) : ProtonTool<OpenPageInput>
{
    /// <summary>The endpoint behind each page, whose permission the page needs, and whether it is Design only.</summary>
    private static readonly IReadOnlyDictionary<ProtonPage, (ProtonEndpoint Endpoint, bool DesignOnly, bool Company, string Label)> Pages =
        new Dictionary<ProtonPage, (ProtonEndpoint, bool, bool, string)>
        {
            [ProtonPage.Dashboard] = (ProtonEndpoint.Of<DashboardController>(nameof(DashboardController.Get)), false, false, "the dashboard"),
            [ProtonPage.Projects] = (ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List)), false, false, "projects"),
            [ProtonPage.Clients] = (ProtonEndpoint.Of<ClientsController>(nameof(ClientsController.List)), false, false, "clients"),
            [ProtonPage.People] = (ProtonEndpoint.Of<EmployeesController>(nameof(EmployeesController.List)), true, false, "people"),
            [ProtonPage.Financials] = (ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary)), false, false, "financials"),
            [ProtonPage.Meetings] = (ProtonEndpoint.Of<MeetingsController>(nameof(MeetingsController.List)), false, false, "meetings"),
            [ProtonPage.Activity] = (ProtonEndpoint.Of<ActivityController>(nameof(ActivityController.List)), true, false, "activity"),
            [ProtonPage.Reports] = (ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Work)), true, false, "reports"),
            [ProtonPage.CompanyOverview] = (ProtonEndpoint.Of<CompanyOverviewController>(nameof(CompanyOverviewController.Get)), false, true, "the company overview"),
            [ProtonPage.CompanyExpenses] = (ProtonEndpoint.Of<OverheadController>(nameof(OverheadController.Get)), false, true, "company expenses"),
        };

    public override string Name => "open_page";

    public override string Description =>
        "Open a page of the app for the user: dashboard, projects (optionally one client's), clients, people, financials " +
        "(optionally a tab), meetings, activity, reports, company overview or company expenses. Only these pages exist " +
        "for Proton; it can't open settings, salaries or any address.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(OpenPageInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (endpoint, designOnly, company, label) = Pages[input.Page];
        await context.RequireAsync(endpoint);

        string? domain = null;
        if (!company)
        {
            domain = designOnly ? DomainCodes.Design : input.Domain?.Code() ?? HomeDomain(context);
            if (domain is null || !context.Access.VisibleDomains.Contains(domain))
            {
                throw new ProtonToolInputException(designOnly ? $"{label} is a Design page, which this user can't see." : "This user can't see that business.");
            }
        }

        ProtonRef? client = null;
        if (input.ClientId is { } clientId && input.Page == ProtonPage.Projects)
        {
            client = await records.FindAsync(ProtonRefType.Client, clientId, context, cancellationToken)
                ?? throw new Exceptions.Base.NotFoundException("Not found.");
            domain = client.Domain;
            label = $"{client.Name}'s projects";
        }

        var command = new ProtonUiCommand(
            ProtonUiCommandType.OpenPage, label, Domain: domain, Page: input.Page, ClientId: client?.Id,
            Tab: input.Page == ProtonPage.Financials ? input.Tab : null);
        return new ProtonToolOutput(
            new UiOutcome(label, "The app opens it when your answer is shown. Say what you opened in a few words."),
            client is null ? [] : [client]) { UiCommand = command };
    }

    /// <summary>The user's own business when they can see it, otherwise the first one they can.</summary>
    private static string? HomeDomain(ProtonToolContext context)
        => context.Access.Domain is { } home && context.Access.VisibleDomains.Contains(home) ? home : context.Access.VisibleDomains.FirstOrDefault();
}

public sealed record ChoiceOption
{
    public required ProtonRefType Type { get; init; }

    public required long Id { get; init; }
}

public sealed record OfferChoicesInput
{
    [Description("The question, short: \"Which Alpha do you mean?\"")]
    public required string Question { get; init; }

    [Description("2 to 6 records a tool returned in this conversation (from find or a list). Leave out when offering replies.")]
    public IReadOnlyList<ChoiceOption>? Options { get; init; }

    [Description("Instead of records: 2 to 6 answers to your question (a period, design or execution, yes or no).")]
    public IReadOnlyList<ProtonQuickReply>? Replies { get; init; }
}

public sealed record ChoicesOffered(string Question, IReadOnlyList<ProtonChoice> Options, string Note);

public sealed record RepliesOffered(string Question, IReadOnlyList<ProtonQuickReplyOption> Replies, string Note);

/// <summary>
/// Lets the user answer with a click instead of typing: a pick between several matches, or one of a few fixed answers
/// to Proton's question. Only records the user's own tools found can be offered, and every label comes from the record
/// or the app, never from the model.
/// </summary>
public sealed class OfferChoicesTool : ProtonTool<OfferChoicesInput>
{
    private const string Shown = "The user sees these as buttons under your answer.";

    public override string Name => "offer_choices";

    public override string Description =>
        "Show buttons to answer your question with a click. Either options: when several records could be what the user " +
        "means (only records find or another tool returned in this conversation). Or replies: when your question has a " +
        "few fixed answers (which period, which business, yes or no). Ask the same question in your answer.";

    public override ProtonEndpoint Endpoint { get; } = ProtonRecords.ProjectGet;

    protected override Task<ProtonToolOutput> RunAsync(OfferChoicesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var question = ProtonText.Cut(input.Question, 200)!;
        if ((input.Options is { Count: > 0 }) == (input.Replies is { Count: > 0 }))
        {
            throw new ProtonToolInputException("Offer either options (records) or replies, not both.");
        }

        return Task.FromResult(input.Replies is { Count: > 0 } replies ? Replies(question, replies, context) : Records(question, input.Options!, context));
    }

    private static ProtonToolOutput Replies(string question, IReadOnlyList<ProtonQuickReply> asked, ProtonToolContext context)
    {
        var replies = asked.Distinct().ToList();
        if (replies.Count is < ProtonUiRules.MinChoices or > ProtonUiRules.MaxChoices)
        {
            throw new ProtonToolInputException($"Offer {ProtonUiRules.MinChoices} to {ProtonUiRules.MaxChoices} replies.");
        }

        if (context.ActionPending && replies.Any(ProtonUiRules.IsYesOrNo))
        {
            throw new ProtonToolInputException("A proposal is waiting for the user's Confirm, so no yes or no buttons: the card is how they answer it.");
        }

        if ((replies.Contains(ProtonQuickReply.Design) && !context.Access.CanSee(DomainCodes.Design))
            || (replies.Contains(ProtonQuickReply.Execution) && !context.Access.CanSee(DomainCodes.Execution)))
        {
            throw new ProtonToolInputException("Offer only the businesses this user can see.");
        }

        var options = replies.Select(ProtonUiRules.Option).ToList();
        return new ProtonToolOutput(new RepliesOffered(question, options, Shown), []) { QuickReplies = options };
    }

    private static ProtonToolOutput Records(string question, IReadOnlyList<ChoiceOption> asked, ProtonToolContext context)
    {
        var options = asked.DistinctBy(o => (o.Type, o.Id)).ToList();
        if (options.Count is < ProtonUiRules.MinChoices or > ProtonUiRules.MaxChoices)
        {
            throw new ProtonToolInputException($"Offer {ProtonUiRules.MinChoices} to {ProtonUiRules.MaxChoices} options.");
        }

        var known = options.Select(o => context.KnownRecord(o.Type, o.Id)).ToList();
        if (known.Any(r => r is null))
        {
            throw new ProtonToolInputException("Offer only records a tool returned in this conversation: call find first.");
        }

        var choices = known.Select(r => new ProtonChoice(r!.Type, r.Id, r.Name, Detail(r), r.Domain)).ToList();
        return new ProtonToolOutput(new ChoicesOffered(question, choices, Shown), known.Select(r => r!).ToList()) { Choices = choices };
    }

    private static string Detail(ProtonRef record)
    {
        var kind = record.Type switch { ProtonRefType.Project => "Project", ProtonRefType.Client => "Client", _ => "Person" };
        return record.Domain is { } domain ? $"{kind} · {char.ToUpperInvariant(domain[0])}{domain[1..]}" : kind;
    }
}

public sealed record SuggestRepliesInput
{
    [Description("1 to 3 short replies the user might send next, in their words and language: \"Show last month too\", \"Open it\".")]
    public required IReadOnlyList<string> Replies { get; init; }
}

public sealed record RepliesSuggested(IReadOnlyList<string> Replies, string Note);

/// <summary>
/// Offers a few next things the user might say, as buttons. A tap sends back only the button's place in the list and
/// the server rebuilds the text, so the composer never holds model text and the turn is known not to be typed. None
/// while a proposal waits for its Confirm.
/// </summary>
public sealed class SuggestRepliesTool : ProtonTool<SuggestRepliesInput>
{
    public override string Name => "suggest_replies";

    public override string Description =>
        "Offer up to 3 short replies the user might send next, as buttons under your answer: natural follow-ups to what " +
        "you just said. Never one that confirms, sends, deletes or changes anything. Only when there are obvious next steps.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override Task<ProtonToolOutput> RunAsync(SuggestRepliesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (context.ActionPending)
        {
            throw new ProtonToolInputException("A proposal is waiting for the user's Confirm, so no suggested replies now.");
        }

        var replies = input.Replies.Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (replies.Count is < 1 or > ProtonUiRules.MaxSuggestions
            || replies.Any(r => r.Length is < 2 or > ProtonUiRules.MaxSuggestionLength || r.Any(char.IsControl)))
        {
            throw new ProtonToolInputException(
                $"Suggest 1 to {ProtonUiRules.MaxSuggestions} replies of at most {ProtonUiRules.MaxSuggestionLength} characters each, on one line.");
        }

        return Task.FromResult(new ProtonToolOutput(new RepliesSuggested(replies, "The user sees these as buttons under your answer."), [])
        {
            Suggestions = replies,
        });
    }
}

public sealed record RefreshInput;

/// <summary>Fetches the page the user is on again, for "refresh" or after something changed outside the app's view.</summary>
public sealed class RefreshPageTool : ProtonTool<RefreshInput>
{
    public override string Name => "refresh_page";

    public override string Description => "Reload the data of the page the user is on, when they ask to refresh it.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override Task<ProtonToolOutput> RunAsync(RefreshInput input, ProtonToolContext context, CancellationToken cancellationToken)
        => Task.FromResult(new ProtonToolOutput(new UiOutcome("this page", "The page reloads its data when your answer is shown."), [])
        {
            UiCommand = ProtonUiCommand.Refresh,
        });
}
