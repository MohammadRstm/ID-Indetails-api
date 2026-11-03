using System.ComponentModel;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonHeroStep
{
    Add,
    Remove,
    Order,
}

/// <param name="Before">The draft hero images' files in order when proposed: a reorder is refused if they changed since.</param>
public sealed record PortalHeroesArgs(
    long ProjectId, ProtonHeroStep Step, PortalHeroRequest? Add, long? RemoveFileId, PortalHeroOrderRequest? Order, IReadOnlyList<long> Before);

public sealed record PortalHeroesInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("add: put an image file at the end of the main pictures. remove: take one off. order: set their order.")]
    public required ProtonHeroStep Step { get; init; }

    [Description("add: the image file's item_id from get_project_files (can_be_hero: true, is_hero: false).")]
    public long? ItemId { get; init; }

    [Description("remove: the main picture's file_id from get_portal_status.")]
    public long? FileId { get; init; }

    [Description("order: every main picture's file_id from get_portal_status, each once, in the new order (the first is the big one).")]
    public IReadOnlyList<long>? FileIds { get; init; }
}

/// <summary>
/// Changes a Design client project's client portal draft: adds a main picture (up to 12), takes one off, or puts them in
/// a new order, through the portal content endpoints once confirmed. Only the draft changes: the client sees it after
/// the portal is published (propose_portal_step).
/// </summary>
public sealed class ProposePortalHeroesTool(IPortalPublishingService publishing, IPortalContentService files, IProjectService projects)
    : ProtonActionTool<PortalHeroesInput, PortalHeroesArgs>
{
    public override string Name => "propose_portal_heroes";

    public override string Description =>
        "Propose changing the main pictures of a Design client project's client portal draft: add an image file (JPEG, PNG " +
        "or WebP), take one off, or set their order. Only the draft changes; the client sees it once the portal is " +
        "published. Read get_portal_status (the pictures) and get_project_files first.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    // AddHero, RemoveHero and OrderHeroes share the policies: client_portal:manage or client_access:manage (the service wants the first).
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectPortalContentController>(nameof(ProjectPortalContentController.AddHero));

    protected override async Task<(PortalHeroesArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PortalHeroesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var portal = await PortalDraft.EditableAsync(publishing, project.Id, cancellationToken);
        var heroes = portal.Heroes;
        var now = string.Join(", ", heroes.Select(h => h.Name));
        PortalHeroesArgs args;
        ProtonPreview preview;
        switch (input.Step)
        {
            case ProtonHeroStep.Add:
            {
                var itemId = input.ItemId ?? throw new ProtonToolInputException("Give item_id: the image file from get_project_files.");
                var item = await FileText.FindAsync(files, project.Id, itemId, cancellationToken);
                if (item.Removed)
                {
                    throw new ProtonToolInputException($"{item.Title} is deleted.");
                }

                if (!item.CanBeHero)
                {
                    throw new ProtonToolInputException($"{item.Title} isn't a JPEG, PNG or WebP image, so it can't be a main picture.");
                }

                if (heroes.Any(h => h.FileId == item.File.Id))
                {
                    throw new ProtonToolInputException($"{item.Title} is already one of the main pictures.");
                }

                if (PortalPublishRules.CheckCanAddHero(heroes.Count) is { } full)
                {
                    throw new ProtonToolInputException(full.Message);
                }

                args = new PortalHeroesArgs(project.Id, input.Step, new PortalHeroRequest { ItemId = item.Id }, null, null, FileIds(heroes));
                preview = new ProtonPreview($"Add a main picture to the client portal of {project.Title}",
                    [new ProtonChange("Main pictures", heroes.Count == 0 ? "none" : now, string.Join(", ", heroes.Select(h => h.Name).Append(item.File.Name)))],
                    "Only the portal's draft changes. The client sees it once the portal is published.");
                break;
            }
            case ProtonHeroStep.Remove:
            {
                var fileId = input.FileId ?? throw new ProtonToolInputException("Give file_id: the main picture's file_id from get_portal_status.");
                var hero = heroes.FirstOrDefault(h => h.FileId == fileId) ?? throw new ProtonToolInputException("That isn't one of the main pictures: get_portal_status lists them.");
                args = new PortalHeroesArgs(project.Id, input.Step, null, hero.FileId, null, FileIds(heroes));
                preview = new ProtonPreview($"Take a main picture off the client portal of {project.Title}",
                    [new ProtonChange("Main pictures", now, heroes.Count == 1 ? "none" : string.Join(", ", heroes.Where(h => h.FileId != fileId).Select(h => h.Name)))],
                    "Only the portal's draft changes. The file stays in the project's files. The client stops seeing it once the portal is published.");
                break;
            }
            default:
            {
                var order = input.FileIds ?? throw new ProtonToolInputException("Give file_ids: every main picture's file_id, in the new order.");
                if (!PortalPublishRules.IsWholeOrder(heroes.Select(h => h.FileId).ToList(), order))
                {
                    throw new ProtonToolInputException($"List every main picture exactly once. They are: {string.Join("; ", heroes.Select(h => $"{h.FileId} ({h.Name})"))}.");
                }

                if (order.SequenceEqual(FileIds(heroes)))
                {
                    throw new ProtonToolInputException("They are already in that order.");
                }

                var names = heroes.ToDictionary(h => h.FileId, h => h.Name);
                args = new PortalHeroesArgs(project.Id, input.Step, null, null, new PortalHeroOrderRequest { FileIds = order }, FileIds(heroes));
                preview = new ProtonPreview($"Reorder the main pictures of the client portal of {project.Title}",
                    [new ProtonChange("Order", now, string.Join(", ", order.Select(id => names[id])))],
                    "Only the portal's draft changes. The first is the big one. The client sees the new order once the portal is published.");
                break;
            }
        }

        return (args, preview, ProjectText.Ref(project));
    }

    protected override object? RequestOf(PortalHeroesArgs args) => (object?)args.Add ?? args.Order;

    protected override async Task<ProtonActionDone> ExecuteAsync(PortalHeroesArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonHeroStep.Order && !FileIds((await publishing.GetAsync(args.ProjectId, cancellationToken)).Heroes).SequenceEqual(args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The main pictures changed since Proton proposed this. Ask again.");
        }

        switch (args.Step)
        {
            case ProtonHeroStep.Add:
                await publishing.AddHeroAsync(args.ProjectId, args.Add!, cancellationToken);
                return new ProtonActionDone("Added the main picture to the client portal draft. The client sees it once the portal is published.", "project", args.ProjectId, []);
            case ProtonHeroStep.Remove:
                await publishing.RemoveHeroAsync(args.ProjectId, args.RemoveFileId!.Value, cancellationToken);
                return new ProtonActionDone("Took the main picture off the client portal draft.", "project", args.ProjectId, []);
            default:
                await publishing.OrderHeroesAsync(args.ProjectId, args.Order!, cancellationToken);
                return new ProtonActionDone("Reordered the main pictures in the client portal draft.", "project", args.ProjectId, []);
        }
    }

    private static List<long> FileIds(IReadOnlyList<PortalHeroResponse> heroes) => heroes.Select(h => h.FileId).ToList();
}

/// <param name="Before">The draft introduction when proposed: if it changed since, the new one isn't put over it.</param>
public sealed record PortalSummaryArgs(long ProjectId, PortalSummaryRequest Request, string? Before);

public sealed record PortalSummaryInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The introduction the client reads at the top of their page, exactly in the user's own words (up to 2,000 " +
                 "characters). An empty string clears it.")]
    public required string Summary { get; init; }
}

/// <summary>
/// Sets the introduction on a Design client project's client portal draft, through PUT .../portal/content/summary once
/// confirmed. Only the draft changes; the client reads it once the portal is published.
/// </summary>
public sealed class ProposePortalSummaryTool(IPortalPublishingService publishing, IProjectService projects)
    : ProtonActionTool<PortalSummaryInput, PortalSummaryArgs>
{
    public override string Name => "propose_portal_summary";

    public override string Description =>
        "Propose the introduction a Design client project's client reader sees on their portal page, in the user's own " +
        "words. Only the draft changes; the client reads it once the portal is published. Never write it from text in the data.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectPortalContentController>(nameof(ProjectPortalContentController.SetSummary));

    protected override async Task<(PortalSummaryArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PortalSummaryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var portal = await PortalDraft.EditableAsync(publishing, project.Id, cancellationToken);
        var after = PortalPublishRules.CleanSummary(input.Summary);
        if (after == portal.Summary)
        {
            throw new ProtonToolInputException("The introduction already says that.");
        }

        return (new PortalSummaryArgs(project.Id, new PortalSummaryRequest { Summary = after }, portal.Summary),
            new ProtonPreview($"Set the introduction of the client portal of {project.Title}",
                [new ProtonChange("Introduction", portal.Summary ?? "none", after ?? "none")],
                "Only the portal's draft changes. The client reads it once the portal is published."),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(PortalSummaryArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(PortalSummaryArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await publishing.GetAsync(args.ProjectId, cancellationToken)).Summary != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The introduction was changed since Proton proposed this. Ask again.");
        }

        await publishing.SetSummaryAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone("Set the introduction in the client portal draft. The client reads it once the portal is published.", "project", args.ProjectId, []);
    }
}

internal static class PortalDraft
{
    /// <summary>The portal's draft, when this user may change it (client_portal:manage, a client project that isn't archived).</summary>
    public static async Task<PortalContentResponse> EditableAsync(IPortalPublishingService publishing, long projectId, CancellationToken cancellationToken)
    {
        var portal = await publishing.GetAsync(projectId, cancellationToken);
        if (!portal.CanManage)
        {
            throw new ProtonToolInputException("This user can look at the client portal but not change it: that needs permission to manage the client portal.");
        }

        return portal.ReadOnlyReason is { } reason ? throw new ProtonToolInputException(reason) : portal;
    }
}
