using System.ComponentModel;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Enums;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Website.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/*
 * The Website family, continued: what the home page's moving strip shows, and a project's pictures (their description,
 * removing one, their order). Adding a picture needs a file from the person's own device, so it stays theirs.
 */

/// <param name="Before">The strip as it was when proposed, project ids in order: if it changed since, the change is refused.</param>
public sealed record WebsiteConveyorArgs(WebsiteConveyorRequest Request, IReadOnlyList<long> Before);

public sealed record WebsiteConveyorInput : ProtonActionInput
{
    [Description("The ids of the website projects the home page's moving strip (conveyor) should show, in order, from get_website_projects: " +
                 "up to 20, all published. It is the whole strip, so start from the current one (strip_position) to add, remove or move one. " +
                 "An empty list empties the strip.")]
    public required IReadOnlyList<long> WebsiteProjectIds { get; init; }
}

/// <summary>Sets which website projects the home page's moving strip shows, and in what order, once confirmed with "moving strip" typed.</summary>
public sealed class ProposeWebsiteConveyorTool(IWebsiteProjectService projects) : ProtonActionTool<WebsiteConveyorInput, WebsiteConveyorArgs>
{
    public const string StripPhrase = "moving strip";

    public override string Name => "propose_website_conveyor";

    public override string Description =>
        "Propose which projects the company's public website shows in the moving strip (the conveyor) on its home page, and " +
        "in what order. The whole strip is set, up to 20 published projects. The user types the phrase on the card to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.SetConveyor));

    protected override async Task<(WebsiteConveyorArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteConveyorInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(cancellationToken);
        var byId = list.Projects.ToDictionary(p => p.Id);
        var chosen = new List<WebsiteProjectResponse>();
        foreach (var id in input.WebsiteProjectIds)
        {
            var project = byId.GetValueOrDefault(id) ?? throw new ProtonToolInputException($"There is no website project {id}: ids come from get_website_projects.");
            if (chosen.Contains(project))
            {
                throw new ProtonToolInputException($"“{project.Title}” is listed twice: a project is in the moving strip once.");
            }

            if (!project.Published)
            {
                throw new ProtonToolInputException($"“{project.Title}” isn't published, so it can't be in the moving strip.");
            }

            chosen.Add(project);
        }

        if (chosen.Count > list.ConveyorLimit)
        {
            throw new ProtonToolInputException($"The moving strip shows at most {list.ConveyorLimit} projects.");
        }

        var before = CurrentStrip(list);
        var after = chosen.Select(p => (p.Id, p.Title)).ToList();
        var lines = WebsiteText.OrderLines(before, after);
        if (lines.Count == 0)
        {
            throw new ProtonToolInputException("That is already the moving strip: say what to change.");
        }

        return (new WebsiteConveyorArgs(new WebsiteConveyorRequest { ProjectIds = after.Select(a => a.Id).ToList() }, before.Select(b => b.Id).ToList()),
            new ProtonPreview("Change the website's moving strip", lines,
                after.Count == 0 ? "The home page's moving strip is emptied as soon as you confirm."
                    : $"The home page shows these {after.Count} projects, in this order, as soon as you confirm.",
                Confirm: ProtonConfirm.Typed, Phrase: StripPhrase, Area: DomainCodes.Website),
            null);
    }

    protected override object RequestOf(WebsiteConveyorArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteConveyorArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var now = (await projects.ListAsync(cancellationToken)).Projects.Where(p => p.ConveyorOrder is not null).OrderBy(p => p.ConveyorOrder).Select(p => p.Id);
        if (!now.SequenceEqual(args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The moving strip changed since Proton proposed this. Ask again.");
        }

        await projects.SetConveyorAsync(args.Request, cancellationToken);
        return new ProtonActionDone(
            args.Request.ProjectIds.Count == 0 ? "The moving strip is empty." : $"The moving strip now shows {args.Request.ProjectIds.Count} projects.",
            "website_conveyor", 0, []);
    }

    private static IReadOnlyList<(long Id, string Name)> CurrentStrip(WebsiteProjectsResponse list)
        => list.Projects.Where(p => p.ConveyorOrder is not null).OrderBy(p => p.ConveyorOrder).Select(p => (p.Id, p.Title)).ToList();
}

public enum ProtonPictureStep
{
    Update,
    Remove,
    Order,
}

/// <param name="Before">The project's last change when proposed: if it changed since, the step is refused.</param>
public sealed record WebsitePictureArgs(
    ProtonPictureStep Step, long ProjectId, long? PictureId, WebsiteImageEditRequest? Edit, WebsiteOrderRequest? Order, DateTime Before);

public sealed record WebsitePictureInput : ProtonActionInput
{
    [Description("update: change a picture's description; remove: take a picture off the project; order: set the order of a project's pictures.")]
    public required ProtonPictureStep Step { get; init; }

    [Description("The website project's id, from get_website_projects.")]
    public required long WebsiteProjectId { get; init; }

    [Description("The picture's id, from get_website_projects with this website_project_id; for update and remove.")]
    public long? PictureId { get; init; }

    [Description("update: the new description of the picture, read out to people who can't see it.")]
    public string? Alt { get; init; }

    [Description("order: the ids of every picture of one list (the gallery, or the concept pictures; they are ordered apart), " +
                 "each once, in the new order.")]
    public IReadOnlyList<long>? PictureIds { get; init; }
}

/// <summary>
/// Changes a picture of a website project's gallery or concept row (its description, removing it, their order), once
/// confirmed with the project's title typed. The card, page and main pictures are changed in the app.
/// </summary>
public sealed class ProposeWebsitePictureTool(IWebsiteProjectService projects) : ProtonActionTool<WebsitePictureInput, WebsitePictureArgs>
{
    public override string Name => "propose_website_picture";

    public override string Description =>
        "Propose changing a picture on a project of the company's public website: the description (alt text) of a gallery or " +
        "concept picture, removing it, or the order of the gallery or the concept pictures. Adding a picture, or changing " +
        "the card, page or main picture, is done in the app. The user types the project's title to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonPictureStep, ProtonEndpoint> Steps = new Dictionary<ProtonPictureStep, ProtonEndpoint>
    {
        [ProtonPictureStep.Update] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.UpdateImage)),
        [ProtonPictureStep.Remove] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.RemoveImage)),
        [ProtonPictureStep.Order] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.OrderImages)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonPictureStep.Update];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsitePictureArgs args) => Steps[args.Step];

    protected override async Task<(WebsitePictureArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsitePictureInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.WebsiteProjectId, cancellationToken);
        var target = new ProtonRef(ProtonRefType.WebsiteProject, project.Id, project.Title, DomainCodes.Website);
        var before = project.UpdatedAt ?? project.CreatedAt;

        if (input.Step == ProtonPictureStep.Order)
        {
            var order = OrderOf(project, input.PictureIds);
            var pictures = order.Concept ? project.ConceptImages : project.Gallery;
            var lines = WebsiteText.OrderLines(pictures.Select(Named).ToList(), order.Ids.Select(id => Named(pictures.Single(p => p.Id == id))).ToList());
            if (lines.Count == 0)
            {
                throw new ProtonToolInputException("That is already the order.");
            }

            return (new WebsitePictureArgs(input.Step, project.Id, null, null, new WebsiteOrderRequest { Ids = order.Ids }, before),
                new ProtonPreview($"Change the order of {(order.Concept ? "the concept pictures" : "the gallery")} of {project.Title}", lines,
                    "The project's page shows them in this order as soon as you confirm.", Confirm: ProtonConfirm.Typed, Phrase: project.Title, Area: DomainCodes.Website),
                target);
        }

        var (picture, where) = Find(project, input.PictureId ?? throw new ProtonToolInputException("Which picture? Give its id, from get_website_projects with this website_project_id."));
        if (input.Step == ProtonPictureStep.Remove)
        {
            return (new WebsitePictureArgs(input.Step, project.Id, picture.Id, null, null, before),
                new ProtonPreview($"Remove a picture from {project.Title}", [new ProtonChange($"Picture in {where}", ProtonText.Cut(picture.Alt, 100), "removed")],
                    "It disappears from the project's page, and the file is deleted a day later.",
                    Confirm: ProtonConfirm.Typed, Phrase: project.Title, Area: DomainCodes.Website, Irreversible: true),
                target);
        }

        var alt = input.Alt?.Trim();
        if (string.IsNullOrEmpty(alt))
        {
            throw new ProtonToolInputException("What should the picture's description say?");
        }

        if (alt == picture.Alt)
        {
            throw new ProtonToolInputException("That changes nothing: the picture already has that description.");
        }

        return (new WebsitePictureArgs(input.Step, project.Id, picture.Id, new WebsiteImageEditRequest { Alt = alt }, null, before),
            new ProtonPreview($"Change a picture's description on {project.Title}", [new ProtonChange($"Description of a picture in {where}", ProtonText.Cut(picture.Alt, 100), alt)],
                "Screen readers read the new description as soon as you confirm.", Confirm: ProtonConfirm.Typed, Phrase: project.Title, Area: DomainCodes.Website),
            target);
    }

    protected override object? RequestOf(WebsitePictureArgs args) => args.Step == ProtonPictureStep.Update ? args.Edit : args.Order;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsitePictureArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(args.ProjectId, cancellationToken);
        if ((project.UpdatedAt ?? project.CreatedAt) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The website project changed since Proton proposed this. Ask again.");
        }

        var (message, changed) = args.Step switch
        {
            ProtonPictureStep.Update => ($"Changed a picture's description on {project.Title}.", await projects.UpdateImageAsync(project.Id, args.PictureId!.Value, args.Edit!, cancellationToken)),
            ProtonPictureStep.Remove => ($"Removed a picture from {project.Title}.", await projects.RemoveImageAsync(project.Id, args.PictureId!.Value, cancellationToken)),
            _ => ($"Changed the order of the pictures of {project.Title}.", await projects.OrderImagesAsync(project.Id, args.Order!, cancellationToken)),
        };
        return new ProtonActionDone(message, "website_project", changed.Id, [new ProtonRef(ProtonRefType.WebsiteProject, changed.Id, changed.Title, DomainCodes.Website)]);
    }

    private static (long Id, string Name) Named(WebsiteImageResponse picture)
        => (picture.Id, string.IsNullOrWhiteSpace(picture.Alt) ? $"Picture {picture.Id}" : ProtonText.Cut(picture.Alt, 60)!);

    private static (WebsiteImageResponse Picture, string Where) Find(WebsiteProjectResponse project, long id)
        => project.Gallery.FirstOrDefault(p => p.Id == id) is { } gallery ? (gallery, "the gallery")
            : project.ConceptImages.FirstOrDefault(p => p.Id == id) is { } concept ? (concept, "the concept pictures")
            : throw new NotFoundException("Picture not found.");

    /// <summary>The gallery or the concept pictures (they are ordered apart), named whole and once each.</summary>
    private static (bool Concept, IReadOnlyList<long> Ids) OrderOf(WebsiteProjectResponse project, IReadOnlyList<long>? ids)
    {
        if (ids is null or { Count: 0 })
        {
            throw new ProtonToolInputException("Give picture_ids: every picture of the gallery, or of the concept pictures, once, in the new order.");
        }

        var concept = project.ConceptImages.Any(p => p.Id == ids[0]);
        var current = (concept ? project.ConceptImages : project.Gallery).Select(p => p.Id).ToList();
        if (!WebsiteRules.IsWholeOrder(current, ids))
        {
            throw new ProtonToolInputException(
                $"Name every picture of {(concept ? "the concept pictures" : "the gallery")} once (and none from the other list). Its pictures are: {string.Join(", ", current)}.");
        }

        return (concept, ids);
    }
}
