using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

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

    [Description("With the files or portal tab only: presentation, model or drawing (a section of the files), and on the portal tab also access (who of the client can see it) or discussion (the chat with the client).")]
    public ProtonProjectPart? Part { get; init; }
}

/// <summary>
/// Opens a project (optionally on a tab), a client, a person or a website project in the app. The record is read first
/// exactly as its own page would read it, so an id the user can't see is "not found" and nothing opens.
/// </summary>
public sealed class OpenRecordTool(ProtonRecords records, ProtonPortalTab portal) : ProtonTool<OpenRecordInput>
{
    private static readonly ProtonEndpoint Requirements = ProtonEndpoint.Of<RequirementsController>(nameof(RequirementsController.List));
    private static readonly ProtonEndpoint Timeline = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.Timeline));
    private static readonly ProtonEndpoint ProjectWork = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.ProjectWork));
    private static readonly ProtonEndpoint Meetings = ProtonEndpoint.Of<MeetingsController>(nameof(MeetingsController.List));

    public override string Name => "open_record";

    public override string Description =>
        "Open a project, client, person or website project in the app for the user (\"take me to…\", \"open…\", \"show me " +
        "its requirements\"). Use the id from find. Projects can open on a tab: requirements or meetings, and on Design " +
        "projects financials (its money), analytics (charts of its work), contributors (who worked on it and how much), " +
        "timeline (its history as a calendar), files (the project's files, optionally one section) or portal (what its " +
        "client sees in the client portal, who of them has access, and the discussion with them). Opening needs no other " +
        "lookup; only read data too if the user also asked a question about it.";

    public override ProtonEndpoint Endpoint { get; } = ProtonRecords.ProjectGet;

    protected override async Task<ProtonToolOutput> RunAsync(OpenRecordInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var record = await records.FindAsync(input.Type, input.Id, context, cancellationToken)
            ?? throw new Exceptions.Base.NotFoundException("Not found.");
        var section = input.Type == ProtonRefType.Project ? input.Section : null;
        var part = section is ProtonProjectSection.Files or ProtonProjectSection.Portal ? input.Part : null;
        var note = "The app opens it when your answer is shown. Say what you opened in a few words.";
        if (section is { } asked && !await CanOpenAsync(asked, part, record, context, cancellationToken))
        {
            note = $"The {asked.ToString().ToLowerInvariant()} tab{(part is { } p ? $" ({p.ToString().ToLowerInvariant()})" : "")} isn't available to this user here, so the project opens on its first tab. Say so.";
            section = null;
            part = null;
        }

        var command = ProtonUiCommand.Open(record, section, part);
        return new ProtonToolOutput(new UiOutcome(Describe(command), note), [record]) { UiCommand = command };
    }

    /// <summary>The project page's own rules for its tabs: a tab the person may not open isn't asked for.</summary>
    private async Task<bool> CanOpenAsync(
        ProtonProjectSection section, ProtonProjectPart? part, ProtonRef project, ProtonToolContext context, CancellationToken cancellationToken)
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
            ProtonProjectSection.Files => await portal.CanOpenFilesAsync(project, part, context),
            ProtonProjectSection.Portal => await portal.CanOpenPortalAsync(project, part, context, cancellationToken),
            _ => false,
        };
    }

    internal static string Describe(ProtonUiCommand command)
        => command.Section is { } section
            ? $"{command.Label}: {section.ToString().ToLowerInvariant()}{(command.Part is { } part ? $", {part.ToString().ToLowerInvariant()}" : "")}"
            : command.Label;
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
            // the Website area: no business of its own, so like the company area it has no domain
            // its content pages read what its projects page reads, behind the same permission (Content can't be named:
            // ControllerBase has methods of that name too)
            [ProtonPage.WebsiteProjects] = (WebsiteRead, false, true, "the website's projects"),
            [ProtonPage.WebsiteAnalytics] = (ProtonEndpoint.Of<WebsiteAnalyticsController>(nameof(WebsiteAnalyticsController.Report)), false, true, "the website's analytics"),
            [ProtonPage.WebsiteServices] = (WebsiteRead, false, true, "the website's services"),
            [ProtonPage.WebsiteLocations] = (WebsiteRead, false, true, "the website's locations"),
            [ProtonPage.WebsiteContact] = (WebsiteRead, false, true, "the website's contact and social links"),
            [ProtonPage.WebsiteSite] = (WebsiteRead, false, true, "the website's site settings"),
            [ProtonPage.WebsiteStaff] = (ProtonEndpoint.Of<EmployeesController>(nameof(EmployeesController.List)), false, true, "the website's staff"),
        };

    private static ProtonEndpoint WebsiteRead => ProtonEndpoint.Of<WebsiteController>(nameof(WebsiteController.Projects));

    /// <summary>The pages of the Website area: only for those who see the website, as its area is.</summary>
    internal static readonly IReadOnlySet<ProtonPage> WebsiteArea = new HashSet<ProtonPage>
    {
        ProtonPage.WebsiteProjects, ProtonPage.WebsiteAnalytics, ProtonPage.WebsiteServices, ProtonPage.WebsiteLocations,
        ProtonPage.WebsiteContact, ProtonPage.WebsiteSite, ProtonPage.WebsiteStaff,
    };

    public override string Name => "open_page";

    public override string Description =>
        "Open a page of the app for the user: dashboard, projects (optionally one client's), clients, people, financials " +
        "(optionally a tab), meetings, activity, reports, settings, notifications, messages, help, and in the company area " +
        "the overview, expenses, schedules, salaries, project pay, Proton's budget, company settings, the profile and the " +
        "start page; for employees their own my_work, my_pay and my_profile; in the Website area (the company's public " +
        "website) its projects, analytics, services, locations, contact (email and social links), site (favicon, under " +
        "construction, watermark check) and staff. Only these pages exist for Proton; it can't open any address. It never reads what salary or pay pages show.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(OpenPageInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (endpoint, designOnly, company, label) = Pages[input.Page];
        if (endpoint is not null)
        {
            await context.RequireAsync(endpoint);
        }

        if (WebsiteArea.Contains(input.Page) && !context.Access.VisibleDomains.Contains(DomainCodes.Website))
        {
            throw new ProtonToolInputException("This user doesn't have the Website area.");
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

    /// <summary>
    /// The user's own business when they can see it, otherwise the first one they can: Design or Execution, never the
    /// Website area, which isn't a business.
    /// </summary>
    internal static string? HomeDomain(ProtonToolContext context)
    {
        static bool Business(string domain) => domain is DomainCodes.Design or DomainCodes.Execution;
        return context.Access.Domain is { } home && Business(home) && context.Access.VisibleDomains.Contains(home)
            ? home
            : context.Access.VisibleDomains.FirstOrDefault(Business);
    }
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
        var kind = record.Type switch { ProtonRefType.Project => "Project", ProtonRefType.Client => "Client", ProtonRefType.Team => "Team chat", ProtonRefType.WebsiteProject => "Website project", _ => "Person" };
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
                 "project_payment_terms, add_project_expense, site_pricing, new_invoice, record_payment, new_statement, " +
                 "view_project_file, file_comments, portal_discussion and portal_preview; a client for edit_client; a person for " +
                 "message_person; a website project for edit_website_project. Leave out for the others.")]
    public long? Id { get; init; }

    [Description("view_project_file and file_comments (required), portal_discussion (optional, to open one file's thread): the project file's item_id, from get_project_files.")]
    public long? ItemId { get; init; }

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
public sealed class OpenSurfaceTool(
    ProtonRecords records, ProtonPortalTab portal, IProjectFileCommentService fileComments, IPortalDiscussionService discussion)
    : ProtonTool<OpenSurfaceInput>
{
    public override string Name => "open_surface";

    public override string Description =>
        "Open a form, dialog or tab for the user (\"open the new project form\", \"let me edit Acme's details\", \"take me " +
        "to the finance settings\", \"I want to message Sara\", \"open the comments on the floor plan\", \"show me the " +
        "discussion with the client\", \"show me the floor plan PDF\", \"open the website project form\"). It only opens it: the user fills it in and " +
        "saves it themselves. Use the record's id from find when the surface is for one. Changes you can propose go on a card instead.";

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
            throw new ProtonToolInputException(only == DomainCodes.Website ? $"{spec.Label} is in the Website area." : $"{spec.Label} is only for {only} projects.");
        }

        var itemId = await ItemOfAsync(input, record, context, cancellationToken);
        var label = record is null ? spec.Label : $"{spec.Label} for {record.Name}";
        var command = new ProtonUiCommand(
            ProtonUiCommandType.OpenSurface, label, record?.Id, domain, Surface: input.Surface,
            SettingsSection: input.Surface == ProtonSurface.SettingsSection ? input.Section ?? ProtonSettingsSection.General : null,
            CompanyTab: input.Surface == ProtonSurface.CompanyOverviewTab ? input.Tab ?? ProtonCompanyTab.Overview : null,
            ItemId: itemId);
        return new ProtonToolOutput(
            new UiOutcome(label, "The app opens it when your answer is shown, without filling or saving anything. Say what you opened."),
            record is null ? [] : [record]) { UiCommand = command };
    }

    /// <summary>
    /// The project file a surface opens on, read as its page reads it, and the checks of a project's portal surfaces:
    /// the discussion opens only for those taking part in it, the preview only on a project with a client.
    /// </summary>
    private async Task<long?> ItemOfAsync(OpenSurfaceInput input, ProtonRef? project, ProtonToolContext context, CancellationToken cancellationToken)
    {
        switch (input.Surface)
        {
            case ProtonSurface.FileComments or ProtonSurface.ViewProjectFile:
                var item = input.ItemId ?? throw new ProtonToolInputException("Which file? Give its item id from the project's files.");
                await fileComments.ListAsync(project!.Id, item, cancellationToken);
                return item;
            case ProtonSurface.PortalDiscussion:
                if (!await portal.TakesPartAsync(project!.Id, cancellationToken))
                {
                    throw new Exceptions.Base.NotFoundException("Not found.");
                }

                if (input.ItemId is { } thread)
                {
                    await discussion.ThreadAsync(Business.Portal.DiscussionSide.Staff, project.Id, thread, cancellationToken);
                }

                return input.ItemId;
            case ProtonSurface.PortalPreview:
                return await portal.CanOpenPortalAsync(project!, ProtonProjectPart.Presentation, context, cancellationToken)
                    ? null
                    : throw new Exceptions.Base.NotFoundException("Not found.");
            default:
                return null;
        }
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

    [Description("The record the form is for: the project for edit_project, report_options, portal_intro, portal_message, materials, " +
                 "payment, project_expense and labour_week; the client, requirement, team, invoice, company expense, website project, " +
                 "service, office or social link being edited; the work entry for work_entry_comment; the file's item id for " +
                 "file_rename and file_comment; the person for schedule and hour_excuse. Leave out for something new.")]
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

    public RequirementFill? Requirement { get; init; }

    public WorkEntryCommentFill? WorkEntryComment { get; init; }

    public ReportOptionsFill? ReportOptions { get; init; }

    public TeamFill? Team { get; init; }

    public ProfileFill? Profile { get; init; }

    public FileRenameFill? FileRename { get; init; }

    public FileCommentFill? FileComment { get; init; }

    public PortalIntroFill? PortalIntro { get; init; }

    public PortalMessageFill? PortalMessage { get; init; }

    public WebsiteProjectFill? WebsiteProject { get; init; }

    public WebsiteServiceFill? WebsiteService { get; init; }

    public WebsiteLocationFill? WebsiteLocation { get; init; }

    public WebsiteSocialLinkFill? WebsiteSocialLink { get; init; }

    public CompanyDetailsFill? CompanyDetails { get; init; }

    public CompanyExpenseFill? CompanyExpense { get; init; }

    public MaterialsFill? Materials { get; init; }

    public InvoiceFill? Invoice { get; init; }

    public PaymentFill? Payment { get; init; }

    public ProjectExpenseFill? ProjectExpense { get; init; }

    public LabourWeekFill? LabourWeek { get; init; }

    public ScheduleFill? Schedule { get; init; }

    public HourExcuseFill? HourExcuse { get; init; }
}

public sealed record MessageDraft
{
    public long? PersonId { get; init; }

    public long? TeamId { get; init; }

    [Description("The message, in the user's voice, from what the user said.")]
    public required string Body { get; init; }
}

/// <summary>What filling a form needs: the endpoint it saves through (new and edited), the user's own words, a family switched on.</summary>
/// <param name="TypedOnly">It sends something in the user's name, reaches clients or the public website: only on a turn they typed.</param>
/// <param name="Family">The whole form is about pay (or money): only while that family of Proton's actions is switched on.</param>
public sealed record ProtonFormSpec(string Label, ProtonEndpoint? New, ProtonEndpoint? Edit = null, bool TypedOnly = false, ProtonFamily? Family = null);

/// <summary>Every form Proton can fill and what filling it needs; a test checks every <see cref="ProtonForm"/> is here.</summary>
public static class ProtonFormSpecs
{
    private static ProtonEndpoint W<T>(string action) where T : Microsoft.AspNetCore.Mvc.ControllerBase => ProtonEndpoint.OfWrite<T>(action);

    public static readonly IReadOnlyDictionary<ProtonForm, ProtonFormSpec> All = new Dictionary<ProtonForm, ProtonFormSpec>
    {
        [ProtonForm.Client] = new("the client form", W<ClientsController>(nameof(ClientsController.Create)), W<ClientsController>(nameof(ClientsController.Update))),
        [ProtonForm.Meeting] = new("the meeting form", W<MeetingsController>(nameof(MeetingsController.Create))),
        [ProtonForm.NewProject] = new("the new project form", W<ProjectsController>(nameof(ProjectsController.Create))),
        [ProtonForm.EditProject] = new("the project details form", W<ProjectsController>(nameof(ProjectsController.Update))),
        [ProtonForm.ReportBug] = new("the bug report form", null, TypedOnly: true),
        [ProtonForm.Invite] = new("the invite form", W<InvitationsController>(nameof(InvitationsController.Create)), TypedOnly: true),
        [ProtonForm.Message] = new("a draft", null, TypedOnly: true),
        [ProtonForm.LogWork] = new("the log work form", W<WorkEntriesController>(nameof(WorkEntriesController.Create))),
        [ProtonForm.Requirement] = new("the requirement form", W<RequirementsController>(nameof(RequirementsController.Create)), W<RequirementsController>(nameof(RequirementsController.Update))),
        [ProtonForm.WorkEntryComment] = new("the work entry's comment", W<WorkEntriesController>(nameof(WorkEntriesController.AddComment)), TypedOnly: true),
        [ProtonForm.ReportOptions] = new("the report's options", ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Report))),
        [ProtonForm.Team] = new("the team's name", W<TeamsController>(nameof(TeamsController.Create)), W<TeamsController>(nameof(TeamsController.Rename))),
        [ProtonForm.Profile] = new("your profile form", ProtonEndpoint.OfRuleBasedWrite<ProfileController>(nameof(ProfileController.UpdateProfile))),
        [ProtonForm.FileRename] = new("the file's rename form", W<ProjectFilesController>(nameof(ProjectFilesController.Update))),
        [ProtonForm.FileComment] = new("the file's comment", W<ProjectFilesController>(nameof(ProjectFilesController.Comment)), TypedOnly: true),
        [ProtonForm.PortalIntro] = new("the client portal's introduction", W<ProjectPortalContentController>(nameof(ProjectPortalContentController.SetSummary)), TypedOnly: true),
        [ProtonForm.PortalMessage] = new("a draft to the client", null, TypedOnly: true),
        [ProtonForm.WebsiteProject] = new("the website project form", W<WebsiteController>(nameof(WebsiteController.CreateProject)), W<WebsiteController>(nameof(WebsiteController.UpdateProject)), TypedOnly: true),
        [ProtonForm.WebsiteService] = new("the website service form", W<WebsiteController>(nameof(WebsiteController.AddService)), W<WebsiteController>(nameof(WebsiteController.UpdateService)), TypedOnly: true),
        [ProtonForm.WebsiteLocation] = new("the website office form", W<WebsiteController>(nameof(WebsiteController.AddLocation)), W<WebsiteController>(nameof(WebsiteController.UpdateLocation)), TypedOnly: true),
        [ProtonForm.WebsiteSocialLink] = new("the website link form", W<WebsiteController>(nameof(WebsiteController.AddSocialLink)), W<WebsiteController>(nameof(WebsiteController.UpdateSocialLink)), TypedOnly: true),
        [ProtonForm.CompanyDetails] = new("the company details form", W<CompanyController>(nameof(CompanyController.UpdateSettings))),
        [ProtonForm.CompanyExpense] = new("the company expense form", W<OverheadController>(nameof(OverheadController.Create)), W<OverheadController>(nameof(OverheadController.Update))),
        [ProtonForm.Materials] = new("the materials sheet", W<MaterialsController>(nameof(MaterialsController.Add))),
        [ProtonForm.Invoice] = new("the invoice form", W<InvoicesController>(nameof(InvoicesController.Create)), W<InvoicesController>(nameof(InvoicesController.Update))),
        [ProtonForm.Payment] = new("the record payment form", W<PaymentsController>(nameof(PaymentsController.Record)), Family: ProtonFamily.Money),
        [ProtonForm.ProjectExpense] = new("the project expense form", W<ExpensesController>(nameof(ExpensesController.Create))),
        [ProtonForm.LabourWeek] = new("the labour week", W<LabourController>(nameof(LabourController.SubmitWeek))),
        [ProtonForm.Schedule] = new("the person's schedule", W<SchedulesController>(nameof(SchedulesController.Set)), Family: ProtonFamily.PayRules),
        [ProtonForm.HourExcuse] = new("the excuse hours form", W<HourExcusesController>(nameof(HourExcusesController.Add)), Family: ProtonFamily.PayRules),
    };

    /// <summary>The forms whose fill id names the record being edited (others name the project or person the form is on).</summary>
    public static readonly IReadOnlySet<ProtonForm> IdEdits = new HashSet<ProtonForm>
    {
        ProtonForm.Client, ProtonForm.Requirement, ProtonForm.Team, ProtonForm.WebsiteProject, ProtonForm.WebsiteService,
        ProtonForm.WebsiteLocation, ProtonForm.WebsiteSocialLink, ProtonForm.CompanyExpense, ProtonForm.Invoice,
    };
}

/// <summary>
/// Fills a form for the user to review and save themselves: the form opened with open_surface (in the same answer or
/// before), its values typed per form, checked here first. The app fills empty, visible fields only, marks them, and
/// never submits; Enter in a filled form waits until the user has touched it. Passwords, permissions, roles, bank
/// details and uploads can't be expressed. Money values only while money actions are on and only amounts the user
/// typed; the pay forms only while pay-rule actions are on. What reaches clients, the public website or someone's
/// inbox needs the user's own words.
/// </summary>
public sealed class FillFormTool(
    ProtonRecords records, IMessagingService messaging, ProtonPortalTab portal, IServiceProvider services, IOptions<ProtonOptions> options)
    : ProtonTool<FillFormInput>
{
    public override string Name => "fill_form";

    public override string Description =>
        "Fill a form for the user to check and save: open it first with open_surface (or open_record or open_page for a " +
        "form on a page, in the same answer is fine). Give only the values for that form, from what the user said or a " +
        "tool returned; leave out anything you don't know. The user presses Save or Send themselves. Money amounts only " +
        "when the user typed them. For changes you can propose on a card, propose instead unless the user asked for the form.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(FillFormInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var fill = new ProtonFormFill(
            input.Form, input.Id, input.Client, input.Meeting, input.NewProject, input.EditProject, input.ReportBug, input.Invite,
            input.Message is { } m ? new MessageFill { Body = m.Body } : null, input.LogWork, input.Requirement, input.WorkEntryComment,
            input.ReportOptions, input.Team, input.Profile, input.FileRename, input.FileComment, input.PortalIntro, input.PortalMessage,
            input.WebsiteProject, input.WebsiteService, input.WebsiteLocation, input.WebsiteSocialLink, input.CompanyDetails,
            input.CompanyExpense, input.Materials, input.Invoice, input.Payment, input.ProjectExpense, input.LabourWeek, input.Schedule,
            input.HourExcuse);
        try
        {
            _ = fill.Values;
        }
        catch (InvalidOperationException)
        {
            throw new ProtonToolInputException($"Give the values of the {input.Form} form, and only those.");
        }

        var problems = ProtonFormRules.Problems(fill);
        if (problems.Count > 0)
        {
            throw new ProtonToolInputException(string.Join(" ", problems));
        }

        // what the form saves through: a new record's endpoint, or an edited one's
        var spec = ProtonFormSpecs.All[input.Form];
        var editing = input.Id is not null && ProtonFormSpecs.IdEdits.Contains(input.Form);
        if ((editing ? spec.Edit ?? spec.New : spec.New) is { } endpoint)
        {
            await context.RequireAsync(endpoint);
        }

        if (spec.TypedOnly && context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("This form sends something in the user's name or shows it to people outside the company, so it is filled only when they ask in their own words.");
        }

        if (spec.Family is { } family && !options.Value.Actions.IsOn(family))
        {
            throw new ProtonToolInputException("This form is about pay or money, which isn't switched on for Proton: the user fills it in themselves. Offer to open it.");
        }

        CheckMoney(fill, context);

        var label = spec.Label;
        ProtonRef? record = null;
        switch (input.Form)
        {
            case ProtonForm.EditProject:
                record = await ProjectAsync(input.Id, context, cancellationToken);
                break;
            case ProtonForm.Client:
                if (input.Client!.Email is { } clientEmail)
                {
                    Typed(clientEmail, context, "an email address");
                }

                if (input.Id is { } clientId)
                {
                    record = await Visible(ProtonRefType.Client, clientId, context, cancellationToken);
                }

                break;
            case ProtonForm.Meeting when input.Meeting!.ProjectId is { } meetingProject:
                _ = await ProjectAsync(meetingProject, context, cancellationToken);
                break;
            case ProtonForm.LogWork when input.LogWork!.ProjectId is { } workProject:
                _ = await ProjectAsync(workProject, context, cancellationToken);
                break;
            case ProtonForm.NewProject when input.NewProject!.ClientId is { } newClient:
                _ = await Visible(ProtonRefType.Client, newClient, context, cancellationToken);
                break;
            case ProtonForm.Invite when input.Invite!.Email is { } email:
                Typed(email, context, "an email address");
                break;
            case ProtonForm.Message:
                (record, label) = await RecipientAsync(input.Message!, context, cancellationToken);
                fill = fill with { Id = record.Id };
                break;
            default:
                record = await CheckNewFormAsync(fill, context, cancellationToken);
                break;
        }

        if (record is not null && input.Form != ProtonForm.Message)
        {
            label = $"{label} for {record.Name}";
        }

        var chat = record?.Type == ProtonRefType.Team ? ProtonSurface.MessageTeam : ProtonSurface.MessagePerson;
        var command = new ProtonUiCommand(ProtonUiCommandType.FillForm, label, record?.Id, record?.Domain, Fill: fill,
            Surface: input.Form == ProtonForm.Message ? chat : null);
        return new ProtonToolOutput(
            new UiOutcome(label, "The app fills the empty fields when your answer is shown; the user checks and saves it. Say so, and say what you left out."),
            record is null ? [] : [record]) { UiCommand = command };
    }

    /// <summary>
    /// Money values only while the Money family is on, on a typed turn, and each an amount the user typed: a figure in
    /// the data (an invoice, a message, a file) never becomes an amount in a form.
    /// </summary>
    private void CheckMoney(ProtonFormFill fill, ProtonToolContext context)
    {
        var money = ProtonFormRules.MoneyIn(fill.Values);
        if (money.Count == 0)
        {
            return;
        }

        if (!options.Value.Actions.IsOn(ProtonFamily.Money))
        {
            throw new ProtonToolInputException("Amounts can't be filled: money isn't switched on for Proton. Fill the rest and leave the amounts to the user.");
        }

        if (context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("Amounts are filled only when the user types them in their own words.");
        }

        if (money.FirstOrDefault(m => !ProtonFormRules.Typed(m.Value, context.TypedText)) is { Field: not null } missing)
        {
            throw new ProtonToolInputException($"{missing.Field}: use only an amount the user typed in this conversation; leave it out otherwise.");
        }
    }

    /// <summary>The records the newer forms name, each read as its own page reads it: not found when the user can't see it.</summary>
    private async Task<ProtonRef?> CheckNewFormAsync(ProtonFormFill fill, ProtonToolContext context, CancellationToken cancellationToken)
    {
        T Service<T>() where T : notnull => services.GetRequiredService<T>();
        switch (fill.Form)
        {
            case ProtonForm.Requirement:
            {
                var r = fill.Requirement!;
                var project = await ProjectAsync(r.ProjectId ?? (fill.Id is null ? null : throw new ProtonToolInputException("Give project_id too.")), context, cancellationToken);
                if (fill.Id is { } requirementId)
                {
                    await Actions.RequirementText.FindAsync(Service<Services.Requirements.Abstraction.IRequirementService>(), project.Id, requirementId, cancellationToken);
                }

                foreach (var person in r.AssigneeIds ?? [])
                {
                    _ = await Visible(ProtonRefType.Person, person, context, cancellationToken);
                }

                return project;
            }
            case ProtonForm.WorkEntryComment:
                _ = await Service<Services.WorkEntries.Abstraction.IWorkEntryQueryService>().GetAsync(fill.Id!.Value, cancellationToken);
                return null;
            case ProtonForm.ReportOptions:
                return await ProjectAsync(fill.Id, context, cancellationToken, DomainCodes.Design);
            case ProtonForm.Team when fill.Id is { } teamId:
                var team = (await Service<Services.Teams.Abstraction.ITeamService>().ListAsync(cancellationToken)).FirstOrDefault(t => t.Id == teamId)
                    ?? throw new Exceptions.Base.NotFoundException("Not found.");
                return new ProtonRef(ProtonRefType.Team, team.Id, team.Name);
            case ProtonForm.FileRename or ProtonForm.FileComment:
            {
                var projectId = fill.FileRename?.ProjectId ?? fill.FileComment!.ProjectId;
                var project = await ProjectAsync(projectId, context, cancellationToken, DomainCodes.Design);
                _ = await Actions.FileText.FindAsync(Service<IPortalContentService>(), project.Id, fill.Id!.Value, cancellationToken);
                return project;
            }
            case ProtonForm.PortalIntro:
            {
                var project = await ProjectAsync(fill.Id, context, cancellationToken, DomainCodes.Design);
                return await portal.CanOpenPortalAsync(project, ProtonProjectPart.Presentation, context, cancellationToken)
                    ? project
                    : throw new Exceptions.Base.NotFoundException("Not found.");
            }
            case ProtonForm.PortalMessage:
            {
                var project = await ProjectAsync(fill.Id, context, cancellationToken, DomainCodes.Design);
                if (!await portal.TakesPartAsync(project.Id, cancellationToken))
                {
                    throw new Exceptions.Base.NotFoundException("Not found.");
                }

                if (fill.PortalMessage!.ItemId is { } thread)
                {
                    await Service<IPortalDiscussionService>().ThreadAsync(Business.Portal.DiscussionSide.Staff, project.Id, thread, cancellationToken);
                }

                return project;
            }
            case ProtonForm.WebsiteProject when fill.Id is { } websiteProjectId:
                return await Visible(ProtonRefType.WebsiteProject, websiteProjectId, context, cancellationToken);
            case ProtonForm.WebsiteService or ProtonForm.WebsiteLocation or ProtonForm.WebsiteSocialLink:
            {
                if (fill.WebsiteSocialLink?.Url is { } url)
                {
                    Typed(url, context, "an address");
                }

                if (fill.Id is { } rowId)
                {
                    var content = await Service<Services.Website.Abstraction.IWebsiteContentService>().GetAsync(cancellationToken);
                    var exists = fill.Form switch
                    {
                        ProtonForm.WebsiteService => content.Services.Any(x => x.Id == rowId),
                        ProtonForm.WebsiteLocation => content.Locations.Any(x => x.Id == rowId),
                        _ => content.SocialLinks.Any(x => x.Id == rowId),
                    };
                    if (!exists)
                    {
                        throw new Exceptions.Base.NotFoundException("Not found.");
                    }
                }

                return null;
            }
            case ProtonForm.CompanyDetails when fill.CompanyDetails!.ContactEmail is { } contact:
                await context.RequireAsync(ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.SetContactEmail)));
                Typed(contact, context, "an email address");
                return null;
            case ProtonForm.CompanyExpense:
            {
                var expense = fill.CompanyExpense!;
                var overhead = Service<Services.Company.Abstraction.IOverheadService>();
                if (fill.Id is { } lineId && (await overhead.GetAsync(cancellationToken)).Lines.All(l => l.Id != lineId))
                {
                    throw new Exceptions.Base.NotFoundException("Not found.");
                }

                if (expense.GroupId is { } groupId && (await overhead.GroupsAsync(cancellationToken)).All(g => g.Id != groupId))
                {
                    throw new Exceptions.Base.NotFoundException("That category doesn't exist.");
                }

                return null;
            }
            case ProtonForm.Materials or ProtonForm.LabourWeek:
                return await ProjectAsync(fill.Id, context, cancellationToken, DomainCodes.Execution);
            case ProtonForm.Invoice:
            {
                if (fill.Id is { } invoiceId)
                {
                    var invoice = await Service<Services.Billing.Abstraction.IInvoiceService>().GetAsync(invoiceId, cancellationToken);
                    if (invoice.Status != Business.Billing.InvoiceStatus.Draft)
                    {
                        throw new ProtonToolInputException("Only a draft invoice can be changed.");
                    }

                    return await ProjectAsync(invoice.Project.Id, context, cancellationToken);
                }

                return await ProjectAsync(fill.Invoice!.ProjectId, context, cancellationToken);
            }
            case ProtonForm.Payment:
            {
                var project = await ProjectAsync(fill.Id, context, cancellationToken);
                if (fill.Payment!.InvoiceId is { } paid
                    && (await Service<Services.Billing.Abstraction.IInvoiceService>().GetAsync(paid, cancellationToken)).Project.Id != project.Id)
                {
                    throw new Exceptions.Base.NotFoundException("That invoice isn't this project's.");
                }

                return project;
            }
            case ProtonForm.ProjectExpense:
                return await ProjectAsync(fill.Id, context, cancellationToken);
            case ProtonForm.Schedule or ProtonForm.HourExcuse:
                return await Visible(ProtonRefType.Person, fill.Id!.Value, context, cancellationToken);
            default:
                return null;
        }
    }

    private async Task<ProtonRef> ProjectAsync(long? id, ProtonToolContext context, CancellationToken cancellationToken, string? onlyIn = null)
    {
        var project = await Visible(ProtonRefType.Project, id ?? throw new ProtonToolInputException("Give the project's id."), context, cancellationToken);
        return onlyIn is null || project.Domain == onlyIn ? project : throw new ProtonToolInputException($"That form is only on {onlyIn} projects.");
    }

    private async Task<ProtonRef> Visible(ProtonRefType type, long id, ProtonToolContext context, CancellationToken cancellationToken)
        => await records.FindAsync(type, id, context, cancellationToken) ?? throw new Exceptions.Base.NotFoundException("Not found.");

    /// <summary>An address or a link reaches outside the company: only one the user typed in this conversation.</summary>
    private static void Typed(string value, ProtonToolContext context, string what)
    {
        if (!context.TypedText.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ProtonToolInputException($"Use {what} only when the user typed it in this conversation; leave it out otherwise.");
        }
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
