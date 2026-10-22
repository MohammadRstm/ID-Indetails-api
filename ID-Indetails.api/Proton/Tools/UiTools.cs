using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Messaging.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>What a UI tool tells the model: what the app will open, and that the user sees it once the answer is shown.</summary>
public sealed record UiOutcome(string Opens, string Note);

public sealed record OpenRecordInput
{
    public required ProtonRefType Type { get; init; }

    [Description("The id from find or another tool. Never guess one.")]
    public required long Id { get; init; }

    [Description("Projects only: the tab to open. Leave out for the project's first tab for this user.")]
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
    private static readonly ProtonEndpoint ProjectWork = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.ProjectWork));
    private static readonly ProtonEndpoint Meetings = ProtonEndpoint.Of<MeetingsController>(nameof(MeetingsController.List));

    public override string Name => "open_record";

    public override string Description =>
        "Open a project, client or person in the app for the user (\"take me to…\", \"open…\", \"show me its requirements\"). " +
        "Use the id from find. Projects can open on a tab: requirements or meetings, and on Design projects financials (its " +
        "money), analytics (charts of its work), contributors (who worked on it and how much) or timeline (its history as a " +
        "calendar). Opening needs no other lookup; only read data too if the user also asked a question about it.";

    public override ProtonEndpoint Endpoint { get; } = ProtonRecords.ProjectGet;

    protected override async Task<ProtonToolOutput> RunAsync(OpenRecordInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var record = await records.FindAsync(input.Type, input.Id, context, cancellationToken)
            ?? throw new Exceptions.Base.NotFoundException("Not found.");
        var section = input.Type == ProtonRefType.Project ? input.Section : null;
        var note = "The app opens it when your answer is shown. Say what you opened in a few words.";
        if (section is { } asked && !await CanOpenAsync(asked, record, context))
        {
            note = $"The {asked.ToString().ToLowerInvariant()} tab isn't available to this user here, so the project opens on its first tab. Say so.";
            section = null;
        }

        var command = ProtonUiCommand.Open(record, section);
        return new ProtonToolOutput(new UiOutcome(Describe(command), note), [record]) { UiCommand = command };
    }

    /// <summary>The project page's own rules for its tabs: a tab the person may not open isn't asked for.</summary>
    private static async Task<bool> CanOpenAsync(ProtonProjectSection section, ProtonRef project, ProtonToolContext context)
    {
        var design = project.Domain == DomainCodes.Design;
        var access = context.Access;
        return section switch
        {
            ProtonProjectSection.Financials => design && access.Has(PermissionCodes.FinancialView),
            // a Design project's overview is the person's own work: those who see money or everyone's work have their own tabs
            ProtonProjectSection.Overview => !design || access.Has(PermissionCodes.WorkEntryViewOwn)
                || (!access.Has(PermissionCodes.FinancialView) && !access.Has(PermissionCodes.WorkEntryViewAll)),
            ProtonProjectSection.Analytics or ProtonProjectSection.Contributors => design && await context.CanCallAsync(ProjectWork),
            ProtonProjectSection.Requirements => await context.CanCallAsync(Requirements),
            ProtonProjectSection.Timeline => design && await context.CanCallAsync(Timeline),
            ProtonProjectSection.Meetings => await context.CanCallAsync(Meetings),
            _ => false,
        };
    }

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
    /// <summary>
    /// The endpoint behind each page, whose permission the page needs (none for pages every signed-in person has, where
    /// the app's own route access still decides), and whether it is Design only or in the company area.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProtonPage, (ProtonEndpoint? Endpoint, bool DesignOnly, bool Company, string Label)> Pages =
        new Dictionary<ProtonPage, (ProtonEndpoint?, bool, bool, string)>
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
            [ProtonPage.Settings] = (null, false, false, "settings"),
            [ProtonPage.Notifications] = (null, false, false, "notifications"),
            [ProtonPage.Messages] = (null, false, false, "messages"),
            [ProtonPage.Help] = (null, false, false, "help"),
            // the figures on these never reach Proton: it only opens them
            [ProtonPage.Schedules] = (ProtonEndpoint.Of<SchedulesController>(nameof(SchedulesController.List)), false, true, "schedules"),
            [ProtonPage.Salaries] = (ProtonEndpoint.Of<SalariesController>(nameof(SalariesController.List)), false, true, "salaries"),
            [ProtonPage.ProjectPay] = (ProtonEndpoint.Of<ProjectPayController>(nameof(ProjectPayController.Get)), false, true, "project pay"),
            [ProtonPage.ProtonBudget] = (ProtonEndpoint.Of<ProtonController>(nameof(ProtonController.Budget)), false, true, "Proton's budget"),
            [ProtonPage.CompanySettings] = (null, false, true, "company settings"),
            [ProtonPage.CompanyProfile] = (null, false, true, "your profile"),
            [ProtonPage.Launcher] = (null, false, true, "the start page"),
            // the person's own pages: their route decides who has them (employees)
            [ProtonPage.MyWork] = (null, false, false, "your work"),
            [ProtonPage.MyPay] = (ProtonEndpoint.Of<MyWorkController>(nameof(MyWorkController.ListEntries)), true, false, "your pay"),
            [ProtonPage.MyProfile] = (null, false, false, "your profile"),
        };

    public override string Name => "open_page";

    public override string Description =>
        "Open a page of the app for the user: dashboard, projects (optionally one client's), clients, people, financials " +
        "(optionally a tab), meetings, activity, reports, settings, notifications, messages, help, and in the company area " +
        "the overview, expenses, schedules, salaries, project pay, Proton's budget, company settings, the profile and the " +
        "start page; for employees their own my_work, my_pay and my_profile. Only these pages exist for Proton; it can't open any address. It never reads what salary or pay pages show.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(OpenPageInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (endpoint, designOnly, company, label) = Pages[input.Page];
        if (endpoint is not null)
        {
            await context.RequireAsync(endpoint);
        }


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
    internal static string? HomeDomain(ProtonToolContext context)
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
    private const string Shown = ProtonButtons.Shown;

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
        var kind = record.Type switch { ProtonRefType.Project => "Project", ProtonRefType.Client => "Client", ProtonRefType.Team => "Team chat", _ => "Person" };
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

        return Task.FromResult(new ProtonToolOutput(new RepliesSuggested(replies, ProtonButtons.Shown), [])
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

public sealed record OpenSurfaceInput
{
    public required ProtonSurface Surface { get; init; }

    [Description("The record it is for, from find or another tool: a project for edit_project, project_report, " +
                 "project_payment_terms, add_project_expense, site_pricing, new_invoice, record_payment and new_statement; " +
                 "a client for edit_client; a person for message_person. Leave out for the others.")]
    public long? Id { get; init; }

    [Description("Which business, for surfaces not tied to a record (new project, new client, invite, new meeting, settings). " +
                 "Leave out for the user's own.")]
    public ProtonDomain? Domain { get; init; }

    [Description("settings_section only.")]
    public ProtonSettingsSection? Section { get; init; }

    [Description("company_overview_tab only.")]
    public ProtonCompanyTab? Tab { get; init; }
}

/// <summary>
/// Opens a form, dialog or drawer for the user, on its own page: only one they could open themselves (the endpoint
/// it saves through, a record they can see), never a one-click confirm. Opening fills and saves nothing; anything that
/// emails someone opens only when the user asked in their own words.
/// </summary>
public sealed class OpenSurfaceTool(ProtonRecords records) : ProtonTool<OpenSurfaceInput>
{
    public override string Name => "open_surface";

    public override string Description =>
        "Open a form, dialog or tab for the user (\"open the new project form\", \"let me edit Acme's details\", \"take me " +
        "to the finance settings\", \"I want to message Sara\"). It only opens it: the user fills it in and saves it themselves. " +
        "Use the record's id from find when the surface is for one. Changes you can propose go on a card instead.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(OpenSurfaceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var spec = ProtonSurfaces.All[input.Surface];
        if (spec.Endpoint is { } endpoint)
        {
            await context.RequireAsync(endpoint);
        }

        if (spec.EmailsSomeone && context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("This form emails someone, so it opens only when the user asks for it in their own words.");
        }

        ProtonRef? record = null;
        string? domain = null;
        if (spec.Record is { } kind)
        {
            record = input.Id is { } id ? await records.FindAsync(kind, id, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.")
                : throw new ProtonToolInputException($"Which {kind.ToString().ToLowerInvariant()}? Give its id from find.");
            domain = record.Domain;
        }
        else if (!spec.Company)
        {
            domain = spec.OnlyIn ?? input.Domain?.Code() ?? OpenPageTool.HomeDomain(context);
        }

        if (!spec.Company && (domain is null || !context.Access.VisibleDomains.Contains(domain)))
        {
            throw new ProtonToolInputException("This user can't see that business.");
        }

        if (spec.OnlyIn is { } only && domain != only)
        {
            throw new ProtonToolInputException($"{spec.Label} is only for {only} projects.");
        }

        var label = record is null ? spec.Label : $"{spec.Label} for {record.Name}";
        var command = new ProtonUiCommand(
            ProtonUiCommandType.OpenSurface, label, record?.Id, domain, Surface: input.Surface,
            SettingsSection: input.Surface == ProtonSurface.SettingsSection ? input.Section ?? ProtonSettingsSection.General : null,
            CompanyTab: input.Surface == ProtonSurface.CompanyOverviewTab ? input.Tab ?? ProtonCompanyTab.Overview : null);
        return new ProtonToolOutput(
            new UiOutcome(label, "The app opens it when your answer is shown, without filling or saving anything. Say what you opened."),
            record is null ? [] : [record]) { UiCommand = command };
    }
}

public sealed record SetPreferenceInput
{
    [Description("Light or dark.")]
    public ProtonTheme? Theme { get; init; }

    [Description("Message sounds on (true) or off (false): only when the user asked in their own words.")]
    public bool? Sounds { get; init; }
}

/// <summary>Changes one of the person's own display choices in this browser: the theme, or message sounds when they asked.</summary>
public sealed class SetPreferenceTool : ProtonTool<SetPreferenceInput>
{
    public override string Name => "set_preference";

    public override string Description => "Switch the app to light or dark, or turn message sounds on or off, when the user asks.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override Task<ProtonToolOutput> RunAsync(SetPreferenceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Theme is null == input.Sounds is null)
        {
            throw new ProtonToolInputException("Change one thing: the theme or the sounds.");
        }

        // a muted chat can hide a message someone sent: only the person's own words turn the sounds off
        if (input.Sounds is not null && context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("Sounds change only when the user asks for it in their own words.");
        }

        var command = input.Theme is { } theme
            ? new ProtonUiCommand(ProtonUiCommandType.SetPreference, theme == ProtonTheme.Dark ? "dark mode" : "light mode", Theme: theme)
            : new ProtonUiCommand(ProtonUiCommandType.SetPreference, input.Sounds == true ? "message sounds on" : "message sounds off", Sounds: input.Sounds);
        return Task.FromResult(new ProtonToolOutput(new UiOutcome(command.Label, "The app changes it when your answer is shown. Say so in a few words."), [])
        {
            UiCommand = command,
        });
    }
}

public sealed record FillFormInput
{
    public required ProtonForm Form { get; init; }

    [Description("The record the form is for: the project for edit_project, the client when editing one. Leave out otherwise.")]
    public long? Id { get; init; }

    public ClientFill? Client { get; init; }

    public MeetingFill? Meeting { get; init; }

    public NewProjectFill? NewProject { get; init; }

    public EditProjectFill? EditProject { get; init; }

    public ReportBugFill? ReportBug { get; init; }

    public InviteFill? Invite { get; init; }

    [Description("The draft for a chat: give person_id or team_id (from find_recipient) and the body.")]
    public MessageDraft? Message { get; init; }

    [Description("The user's own work for the log form.")]
    public LogWorkFill? LogWork { get; init; }
}

public sealed record MessageDraft
{
    public long? PersonId { get; init; }

    public long? TeamId { get; init; }

    [Description("The message, in the user's voice, from what the user said.")]
    public required string Body { get; init; }
}

/// <summary>
/// Fills a form for the user to review and save themselves: the form opened with open_surface (in the same answer or
/// before), its values typed per form, checked here first. The app fills empty, visible fields only, marks them, and
/// never submits; Enter in a filled form waits until the user has touched it. Nothing about money, pay, passwords or
/// permissions can be expressed. A draft message, an invitation and a bug report need the user's own words.
/// </summary>
public sealed class FillFormTool(ProtonRecords records, IMessagingService messaging) : ProtonTool<FillFormInput>
{
    public override string Name => "fill_form";

    public override string Description =>
        "Fill a form for the user to check and save: open it first with open_surface (in the same answer is fine). " +
        "Give only the values for that form, from what the user said or a tool returned; leave out anything you don't " +
        "know. The user presses Save or Send themselves. For changes you can propose on a card, propose instead unless " +
        "the user asked for the form.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    private static readonly IReadOnlyDictionary<ProtonForm, ProtonSurface> SurfaceOf = new Dictionary<ProtonForm, ProtonSurface>
    {
        [ProtonForm.Meeting] = ProtonSurface.NewMeeting,
        [ProtonForm.NewProject] = ProtonSurface.NewProject,
        [ProtonForm.EditProject] = ProtonSurface.EditProject,
        [ProtonForm.ReportBug] = ProtonSurface.ReportBug,
        [ProtonForm.Invite] = ProtonSurface.InvitePerson,
        [ProtonForm.LogWork] = ProtonSurface.LogWork,
    };

    protected override async Task<ProtonToolOutput> RunAsync(FillFormInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var given = new object?[] { input.Client, input.Meeting, input.NewProject, input.EditProject, input.ReportBug, input.Invite, input.Message, input.LogWork }
            .Count(v => v is not null);
        if (given != 1)
        {
            throw new ProtonToolInputException($"Give the values of the {input.Form} form, and only those.");
        }

        var fill = new ProtonFormFill(input.Form, input.Id, input.Client, input.Meeting, input.NewProject, input.EditProject, input.ReportBug, input.Invite,
            input.Message is { } m ? new MessageFill { Body = m.Body } : null, input.LogWork);
        var problems = ProtonFormRules.Problems(fill);
        if (problems.Count > 0)
        {
            throw new ProtonToolInputException(string.Join(" ", problems));
        }

        // what the form saves through, as the surface it opens in needs
        if (input.Form == ProtonForm.Client)
        {
            await context.RequireAsync(input.Id is null
                ? ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Create))
                : ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Update)));
        }
        else if (SurfaceOf.TryGetValue(input.Form, out var surface) && ProtonSurfaces.All[surface].Endpoint is { } endpoint)
        {
            await context.RequireAsync(endpoint);
        }

        if (input.Form is ProtonForm.ReportBug or ProtonForm.Invite or ProtonForm.Message && context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("This form sends something in the user's name, so it is filled only when they ask in their own words.");
        }

        var label = $"the {Words(input.Form)} form";
        ProtonRef? record = null;
        switch (input.Form)
        {
            case ProtonForm.EditProject:
                record = (input.Id is { } projectId ? await records.FindAsync(ProtonRefType.Project, projectId, context, cancellationToken) : null)
                    ?? throw new Exceptions.Base.NotFoundException("Not found.");
                break;
            case ProtonForm.Client when input.Id is { } clientId:
                record = await records.FindAsync(ProtonRefType.Client, clientId, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.");
                break;
            case ProtonForm.Meeting when input.Meeting!.ProjectId is { } meetingProject:
                _ = await records.FindAsync(ProtonRefType.Project, meetingProject, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.");
                break;
            case ProtonForm.LogWork when input.LogWork!.ProjectId is { } workProject:
                _ = await records.FindAsync(ProtonRefType.Project, workProject, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.");
                break;
            case ProtonForm.NewProject when input.NewProject!.ClientId is { } newClient:
                _ = await records.FindAsync(ProtonRefType.Client, newClient, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.");
                break;
            case ProtonForm.Invite when input.Invite!.Email is { } email && !context.TypedText.Contains(email, StringComparison.OrdinalIgnoreCase):
                throw new ProtonToolInputException("Use an email address only when the user typed it in this conversation; leave it out otherwise.");
            case ProtonForm.Message:
                (record, label) = await RecipientAsync(input.Message!, context, cancellationToken);
                fill = fill with { Id = record.Id };
                break;
        }

        var chat = record?.Type == ProtonRefType.Team ? ProtonSurface.MessageTeam : ProtonSurface.MessagePerson;
        var command = new ProtonUiCommand(ProtonUiCommandType.FillForm, label, record?.Id, record?.Domain, Fill: fill,
            Surface: input.Form == ProtonForm.Message ? chat : null);
        return new ProtonToolOutput(
            new UiOutcome(label, "The app fills the empty fields when your answer is shown; the user checks and saves it. Say so, and say what you left out."),
            record is null ? [] : [record]) { UiCommand = command };
    }

    /// <summary>
    /// A draft goes only to someone the user may message (a contact, or a team chat they are in) and named in their own
    /// words, as a message sent with Proton does: text in the data can't choose who a draft is for.
    /// </summary>
    private async Task<(ProtonRef Recipient, string Label)> RecipientAsync(MessageDraft draft, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (draft.PersonId is null == draft.TeamId is null)
        {
            throw new ProtonToolInputException("Give person_id or team_id, from find_recipient.");
        }

        ProtonRef recipient;
        if (draft.PersonId is { } personId)
        {
            var contact = (await messaging.ContactsAsync(cancellationToken)).FirstOrDefault(c => c.Id == personId)
                ?? throw new Exceptions.Base.NotFoundException("Not someone this user can message.");
            recipient = new ProtonRef(ProtonRefType.Person, contact.Id, contact.Name, contact.Domain);
        }
        else
        {
            recipient = await records.FindAsync(ProtonRefType.Team, draft.TeamId!.Value, context, cancellationToken)
                ?? throw new Exceptions.Base.NotFoundException("Not a team chat this user is in.");
        }

        if (!Actions.MessageText.Named(recipient.Name, context.TypedText))
        {
            throw new ProtonToolInputException("Draft only to someone the user named: ask them who it is for in their own words.");
        }

        return (recipient, $"a draft to {recipient.Name}");
    }

    private static string Words(ProtonForm form) => form switch
    {
        ProtonForm.NewProject => "new project",
        ProtonForm.EditProject => "project details",
        ProtonForm.ReportBug => "bug report",
        ProtonForm.LogWork => "log work",
        _ => form.ToString().ToLowerInvariant(),
    };
}

internal static class ProtonButtons
{
    /// <summary>
    /// What the model is told once it has added buttons. It sometimes writes its answer only in its thinking and then
    /// calls a button tool, believing the user has read it: they haven't, so it is told to write the answer now.
    /// </summary>
    public const string Shown =
        "The user sees these as buttons under your answer. They have seen nothing else yet, and never see tool results: " +
        "if you haven't written your answer in your reply text, write all of it now (not \"above\").";
}
