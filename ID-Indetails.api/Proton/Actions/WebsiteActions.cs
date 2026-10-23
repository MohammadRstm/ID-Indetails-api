using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Website.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/*
 * The Website family: changes to the company's public website, which anyone on the internet sees. Every one is strong
 * (the user types a phrase), only on a turn the user typed, and the family is off until it is switched on
 * (Proton:Actions:Website). Pictures, the favicon and social links stay the person's own (ProtonCoverage).
 */

public enum ProtonWebsiteStep
{
    Add,
    Update,
    Delete,
}

/// <param name="Before">The project's last change when proposed: if it changed since, the update or delete is refused.</param>
public sealed record WebsiteProjectArgs(ProtonWebsiteStep Step, long? Id, WebsiteProjectRequest? Request, DateTime? Before);

public sealed record WebsiteProjectInput : ProtonActionInput
{
    [Description("add a new website project, update one, or delete one.")]
    public required ProtonWebsiteStep Step { get; init; }

    [Description("The website project's id, from find (type website_project) or get_website_projects; for update and delete.")]
    public long? WebsiteProjectId { get; init; }

    [Description("For add, required; for update, only to change it.")]
    public string? Title { get; init; }

    [Description("Its kind as the website shows it: villa, residential, hotel, office… For add, required.")]
    public string? Type { get; init; }

    [Description("in_progress or completed. For add, required.")]
    public WebsiteProjectStatus? Status { get; init; }

    [Description("The year shown. For add, required.")]
    public int? Year { get; init; }

    public string? Client { get; init; }

    [Description("A city of the country, as the website lists them.")]
    public string? City { get; init; }

    public string? Country { get; init; }

    [Description("What the website says about it.")]
    public string? Description { get; init; }

    [Description("Show it on the website (true) or keep it a draft (false). Publishing needs its pictures, which only a person adds.")]
    public bool? Published { get; init; }
}

/// <summary>
/// Adds, changes or deletes one of the website's projects, through the Website area's endpoints once confirmed with its
/// title typed. A change starts from the project as it is: pictures, the map point and the link stay as they are.
/// </summary>
public sealed class ProposeWebsiteProjectTool(IWebsiteProjectService projects) : ProtonActionTool<WebsiteProjectInput, WebsiteProjectArgs>
{
    public override string Name => "propose_website_project";

    public override string Description =>
        "Propose adding, changing or deleting a project on the company's public website (not a Design or Execution " +
        "project). Only what the user asked to change; pictures stay theirs to add. The user types the project's title to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonWebsiteStep, ProtonEndpoint> Steps = new Dictionary<ProtonWebsiteStep, ProtonEndpoint>
    {
        [ProtonWebsiteStep.Add] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.CreateProject)),
        [ProtonWebsiteStep.Update] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.UpdateProject)),
        [ProtonWebsiteStep.Delete] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.DeleteProject)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonWebsiteStep.Update];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsiteProjectArgs args) => Steps[args.Step];

    protected override async Task<(WebsiteProjectArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteProjectInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Step == ProtonWebsiteStep.Add)
        {
            var request = new WebsiteProjectRequest
            {
                Title = input.Title?.Trim() ?? "", Type = input.Type?.Trim() ?? "", Status = input.Status, Year = input.Year,
                Client = WebsiteText.Clean(input.Client), City = WebsiteText.Clean(input.City), Country = WebsiteText.Clean(input.Country),
                Description = WebsiteText.Clean(input.Description), Published = input.Published ?? false,
            };
            return (new WebsiteProjectArgs(input.Step, null, request, null),
                new ProtonPreview($"Add {request.Title} to the website", WebsiteText.Lines(null, request),
                    request.Published ? "It shows on the public website as soon as you confirm." : "It is added as a draft: the website doesn't show it yet.",
                    Confirm: ProtonConfirm.Typed, Phrase: request.Title, Area: DomainCodes.Website),
                null);
        }

        var project = await projects.GetAsync(input.WebsiteProjectId ?? throw new ProtonToolInputException("Which website project? Give its id."), cancellationToken);
        var target = new ProtonRef(ProtonRefType.WebsiteProject, project.Id, project.Title, DomainCodes.Website);
        if (input.Step == ProtonWebsiteStep.Delete)
        {
            return (new WebsiteProjectArgs(input.Step, project.Id, null, project.UpdatedAt ?? project.CreatedAt),
                new ProtonPreview($"Delete {project.Title} from the website", [new ProtonChange("Website project", project.Title, "deleted")],
                    project.Published ? "It disappears from the public website, with its pictures." : "It is a draft: the website doesn't show it.",
                    Confirm: ProtonConfirm.Typed, Phrase: project.Title, Area: DomainCodes.Website, Irreversible: true),
                target);
        }

        var before = WebsiteText.RequestOf(project);
        var placeChanged = input.City is not null || input.Country is not null;
        var after = before with
        {
            Title = input.Title?.Trim() ?? before.Title,
            Type = input.Type?.Trim() ?? before.Type,
            Status = input.Status ?? before.Status,
            Year = input.Year ?? before.Year,
            Client = input.Client is null ? before.Client : WebsiteText.Clean(input.Client),
            City = input.City is null ? before.City : WebsiteText.Clean(input.City),
            Country = input.Country is null ? before.Country : WebsiteText.Clean(input.Country),
            // a new place gets a point there; otherwise the point is kept as it is
            Latitude = placeChanged ? null : before.Latitude,
            Longitude = placeChanged ? null : before.Longitude,
            Description = input.Description is null ? before.Description : WebsiteText.Clean(input.Description),
            Published = input.Published ?? before.Published,
        };
        var changes = WebsiteText.Lines(before, after);
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("That changes nothing: say what to change.");
        }

        return (new WebsiteProjectArgs(input.Step, project.Id, after, project.UpdatedAt ?? project.CreatedAt),
            new ProtonPreview($"Change {project.Title} on the website", changes,
                after.Published ? "The public website shows the change as soon as you confirm." : null,
                Confirm: ProtonConfirm.Typed, Phrase: project.Title, Area: DomainCodes.Website),
            target);
    }

    protected override object? RequestOf(WebsiteProjectArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteProjectArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonWebsiteStep.Add)
        {
            var created = await projects.CreateAsync(args.Request!, cancellationToken);
            return new ProtonActionDone($"Added {created.Title} to the website.", "website_project", created.Id,
                [new ProtonRef(ProtonRefType.WebsiteProject, created.Id, created.Title, DomainCodes.Website)]);
        }

        var project = await projects.GetAsync(args.Id!.Value, cancellationToken);
        if ((project.UpdatedAt ?? project.CreatedAt) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The website project changed since Proton proposed this. Ask again.");
        }

        if (args.Step == ProtonWebsiteStep.Delete)
        {
            await projects.DeleteAsync(project.Id, cancellationToken);
            return new ProtonActionDone($"Deleted {project.Title} from the website.", "website_project", project.Id, []);
        }

        var updated = await projects.UpdateAsync(project.Id, args.Request!, cancellationToken);
        return new ProtonActionDone($"Changed {updated.Title} on the website.", "website_project", updated.Id,
            [new ProtonRef(ProtonRefType.WebsiteProject, updated.Id, updated.Title, DomainCodes.Website)]);
    }
}

public sealed record UnderConstructionInput : ProtonActionInput
{
    [Description("true: show only the under-construction page; false: bring the whole website back.")]
    public required bool UnderConstruction { get; init; }
}

/// <summary>Takes the public website down to its under-construction page, or brings it back, once confirmed with a phrase typed.</summary>
public sealed class ProposeUnderConstructionTool(IWebsiteContentService content) : ProtonActionTool<UnderConstructionInput, WebsiteUnderConstructionRequest>
{
    public const string DownPhrase = "under construction";
    public const string UpPhrase = "website live";

    public override string Name => "propose_website_under_construction";

    public override string Description =>
        "Propose putting the company's public website under construction (visitors then see only its construction, " +
        "contact and privacy pages) or bringing it back. The user types a phrase to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.SetUnderConstruction));

    protected override async Task<(WebsiteUnderConstructionRequest, ProtonPreview, ProtonRef?)> ProposeAsync(
        UnderConstructionInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await content.GetAsync(cancellationToken);
        if ((current.UnderConstructionSince is not null) == input.UnderConstruction)
        {
            throw new ProtonToolInputException(input.UnderConstruction ? "The website is already under construction." : "The website is already live.");
        }

        return (new WebsiteUnderConstructionRequest { UnderConstruction = input.UnderConstruction },
            input.UnderConstruction
                ? new ProtonPreview("Put the website under construction", [new ProtonChange("Website", "live", "under construction")],
                    "Visitors see only the construction, contact and privacy pages until it is brought back.",
                    Confirm: ProtonConfirm.Typed, Phrase: DownPhrase, Area: DomainCodes.Website)
                : new ProtonPreview("Bring the website back", [new ProtonChange("Website", "under construction", "live")],
                    "Every page shows again as soon as you confirm.", Confirm: ProtonConfirm.Typed, Phrase: UpPhrase, Area: DomainCodes.Website),
            null);
    }

    protected override object RequestOf(WebsiteUnderConstructionRequest args) => args;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteUnderConstructionRequest args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await content.SetUnderConstructionAsync(args, cancellationToken);
        return new ProtonActionDone(args.UnderConstruction ? "The website is under construction." : "The website is live again.", "website", 0, []);
    }
}

/// <param name="Before">The service as it was when proposed, for an update or delete: if it changed since, the step is refused.</param>
public sealed record WebsiteServiceArgs(ProtonWebsiteStep Step, long? Id, WebsiteServiceRequest? Request, WebsiteServiceRequest? Before);

public sealed record WebsiteServiceInput : ProtonActionInput
{
    [Description("add, update or delete one of the services the website lists.")]
    public required ProtonWebsiteStep Step { get; init; }

    [Description("The service's id, from get_website_content; for update and delete.")]
    public long? ServiceId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }
}

/// <summary>Adds, changes or removes one of the services the public website lists, once confirmed with its title typed.</summary>
public sealed class ProposeWebsiteServiceTool(IWebsiteContentService content) : ProtonActionTool<WebsiteServiceInput, WebsiteServiceArgs>
{
    public override string Name => "propose_website_service";

    public override string Description =>
        "Propose adding, changing or removing one of the services the company's public website lists. The user types the service's title to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonWebsiteStep, ProtonEndpoint> Steps = new Dictionary<ProtonWebsiteStep, ProtonEndpoint>
    {
        [ProtonWebsiteStep.Add] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.AddService)),
        [ProtonWebsiteStep.Update] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.UpdateService)),
        [ProtonWebsiteStep.Delete] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.DeleteService)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonWebsiteStep.Update];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsiteServiceArgs args) => Steps[args.Step];

    protected override async Task<(WebsiteServiceArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteServiceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Step == ProtonWebsiteStep.Add)
        {
            var added = new WebsiteServiceRequest { Title = input.Title?.Trim() ?? "", Description = input.Description?.Trim() ?? "" };
            return (new WebsiteServiceArgs(input.Step, null, added, null),
                new ProtonPreview("Add a service to the website", [new ProtonChange("Service", null, added.Title), new ProtonChange("Description", null, added.Description)],
                    Confirm: ProtonConfirm.Typed, Phrase: added.Title, Area: DomainCodes.Website),
                null);
        }

        var service = (await content.GetAsync(cancellationToken)).Services.FirstOrDefault(s => s.Id == input.ServiceId)
            ?? throw new NotFoundException("Service not found.");
        var before = new WebsiteServiceRequest { Title = service.Title, Description = service.Description };
        if (input.Step == ProtonWebsiteStep.Delete)
        {
            return (new WebsiteServiceArgs(input.Step, service.Id, null, before),
                new ProtonPreview("Remove a service from the website", [new ProtonChange("Service", service.Title, "removed")],
                    Confirm: ProtonConfirm.Typed, Phrase: service.Title, Area: DomainCodes.Website, Irreversible: true),
                null);
        }

        var after = new WebsiteServiceRequest { Title = input.Title?.Trim() ?? service.Title, Description = input.Description?.Trim() ?? service.Description };
        var lines = new List<ProtonChange>();
        if (after.Title != before.Title) lines.Add(new ProtonChange("Title", before.Title, after.Title));
        if (after.Description != before.Description) lines.Add(new ProtonChange("Description", before.Description, after.Description));
        return lines.Count == 0
            ? throw new ProtonToolInputException("That changes nothing: say what to change.")
            : (new WebsiteServiceArgs(input.Step, service.Id, after, before),
                new ProtonPreview($"Change the service {service.Title} on the website", lines, Confirm: ProtonConfirm.Typed, Phrase: service.Title, Area: DomainCodes.Website),
                null);
    }

    protected override object? RequestOf(WebsiteServiceArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteServiceArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonWebsiteStep.Add)
        {
            await content.AddServiceAsync(args.Request!, cancellationToken);
            return new ProtonActionDone($"Added the service {args.Request!.Title}.", "website_service", 0, []);
        }

        var service = (await content.GetAsync(cancellationToken)).Services.FirstOrDefault(s => s.Id == args.Id) ?? throw new NotFoundException("Service not found.");
        if (service.Title != args.Before!.Title || service.Description != args.Before.Description)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The service changed since Proton proposed this. Ask again.");
        }

        if (args.Step == ProtonWebsiteStep.Delete)
        {
            await content.DeleteServiceAsync(service.Id, cancellationToken);
            return new ProtonActionDone($"Removed the service {service.Title}.", "website_service", service.Id, []);
        }

        await content.UpdateServiceAsync(service.Id, args.Request!, cancellationToken);
        return new ProtonActionDone($"Changed the service {args.Request!.Title}.", "website_service", service.Id, []);
    }
}

/// <param name="Before">The office as it was when proposed, for an update or delete: if it changed since, the step is refused.</param>
public sealed record WebsiteLocationArgs(ProtonWebsiteStep Step, long? Id, WebsiteLocationRequest? Request, WebsiteLocationRequest? Before);

public sealed record WebsiteLocationInput : ProtonActionInput
{
    [Description("add, update or delete one of the offices the website lists.")]
    public required ProtonWebsiteStep Step { get; init; }

    [Description("The office's id, from get_website_content; for update and delete.")]
    public long? LocationId { get; init; }

    [Description("A city of the country, as the website lists them. For add, required.")]
    public string? City { get; init; }

    [Description("For add, required.")]
    public string? Country { get; init; }

    public string? Address { get; init; }

    public string? Phone { get; init; }
}

/// <summary>Adds, changes or removes one of the offices the public website lists, once confirmed with its city typed.</summary>
public sealed class ProposeWebsiteLocationTool(IWebsiteContentService content) : ProtonActionTool<WebsiteLocationInput, WebsiteLocationArgs>
{
    public override string Name => "propose_website_location";

    public override string Description =>
        "Propose adding, changing or removing one of the offices the company's public website lists (its city, address " +
        "and phone). The user types the office's city to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonWebsiteStep, ProtonEndpoint> Steps = new Dictionary<ProtonWebsiteStep, ProtonEndpoint>
    {
        [ProtonWebsiteStep.Add] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.AddLocation)),
        [ProtonWebsiteStep.Update] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.UpdateLocation)),
        [ProtonWebsiteStep.Delete] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.DeleteLocation)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonWebsiteStep.Update];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsiteLocationArgs args) => Steps[args.Step];

    protected override async Task<(WebsiteLocationArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteLocationInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Step == ProtonWebsiteStep.Add)
        {
            var added = new WebsiteLocationRequest
            {
                City = WebsiteText.Clean(input.City), Country = WebsiteText.Clean(input.Country), Address = input.Address?.Trim() ?? "", Phone = input.Phone?.Trim() ?? "",
            };
            if (added.City is null || added.Country is null)
            {
                throw new ProtonToolInputException("Give the office's city and country.");
            }

            return (new WebsiteLocationArgs(input.Step, null, added, null),
                new ProtonPreview("Add an office to the website",
                    [new ProtonChange("City", null, $"{added.City}, {added.Country}"), new ProtonChange("Address", null, added.Address), new ProtonChange("Phone", null, added.Phone)],
                    Confirm: ProtonConfirm.Typed, Phrase: added.City, Area: DomainCodes.Website),
                null);
        }

        var office = (await content.GetAsync(cancellationToken)).Locations.FirstOrDefault(l => l.Id == input.LocationId)
            ?? throw new NotFoundException("Office not found.");
        var before = WebsiteText.RequestOf(office);
        if (input.Step == ProtonWebsiteStep.Delete)
        {
            return (new WebsiteLocationArgs(input.Step, office.Id, null, before),
                new ProtonPreview("Remove an office from the website", [new ProtonChange("Office", $"{office.City}, {office.Address}", "removed")],
                    Confirm: ProtonConfirm.Typed, Phrase: office.City, Area: DomainCodes.Website, Irreversible: true),
                null);
        }

        var placeChanged = input.City is not null || input.Country is not null;
        var after = before with
        {
            City = input.City is null ? before.City : WebsiteText.Clean(input.City),
            Country = input.Country is null ? before.Country : WebsiteText.Clean(input.Country),
            Latitude = placeChanged ? null : before.Latitude,
            Longitude = placeChanged ? null : before.Longitude,
            Address = input.Address?.Trim() ?? before.Address,
            Phone = input.Phone?.Trim() ?? before.Phone,
        };
        var lines = new List<ProtonChange>();
        if (after.City != before.City || after.Country != before.Country) lines.Add(new ProtonChange("City", $"{before.City}, {before.Country}", $"{after.City}, {after.Country}"));
        if (after.Address != before.Address) lines.Add(new ProtonChange("Address", before.Address, after.Address));
        if (after.Phone != before.Phone) lines.Add(new ProtonChange("Phone", before.Phone, after.Phone));
        return lines.Count == 0
            ? throw new ProtonToolInputException("That changes nothing: say what to change.")
            : (new WebsiteLocationArgs(input.Step, office.Id, after, before),
                new ProtonPreview($"Change the {office.City} office on the website", lines, Confirm: ProtonConfirm.Typed, Phrase: office.City, Area: DomainCodes.Website),
                null);
    }

    protected override object? RequestOf(WebsiteLocationArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteLocationArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonWebsiteStep.Add)
        {
            await content.AddLocationAsync(args.Request!, cancellationToken);
            return new ProtonActionDone($"Added the {args.Request!.City} office.", "website_location", 0, []);
        }

        var office = (await content.GetAsync(cancellationToken)).Locations.FirstOrDefault(l => l.Id == args.Id) ?? throw new NotFoundException("Office not found.");
        if (WebsiteText.RequestOf(office) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The office changed since Proton proposed this. Ask again.");
        }

        if (args.Step == ProtonWebsiteStep.Delete)
        {
            await content.DeleteLocationAsync(office.Id, cancellationToken);
            return new ProtonActionDone($"Removed the {office.City} office.", "website_location", office.Id, []);
        }

        await content.UpdateLocationAsync(office.Id, args.Request!, cancellationToken);
        return new ProtonActionDone($"Changed the {args.Request!.City} office.", "website_location", office.Id, []);
    }
}

internal static class WebsiteText
{
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The project as its form would send it back unchanged: pictures, point and link included.</summary>
    public static WebsiteProjectRequest RequestOf(WebsiteProjectResponse p) => new()
    {
        Title = p.Title, Type = p.Type, Status = p.Status, Year = p.Year, Client = p.Client, City = p.City, Country = p.Country,
        Latitude = p.Latitude, Longitude = p.Longitude, Description = p.Description, ConceptDescription = p.ConceptDescription,
        ProjectId = p.LinkedProject?.Id, ThumbnailFileId = p.ThumbnailFileId, HeroFileId = p.HeroFileId, HeroAlt = p.HeroAlt, Published = p.Published,
    };

    public static WebsiteLocationRequest RequestOf(WebsiteLocationResponse l) => new()
    {
        City = l.City, Country = l.Country, Latitude = l.Latitude, Longitude = l.Longitude, Address = l.Address, Phone = l.Phone,
    };

    /// <summary>The card's lines: every field set for a new project, only the changed ones for a change.</summary>
    public static IReadOnlyList<ProtonChange> Lines(WebsiteProjectRequest? before, WebsiteProjectRequest after)
    {
        var lines = new List<ProtonChange>();
        void Line(string label, string? was, string? now)
        {
            // a new project lists what it is given; a change lists what differs
            if (before is null ? now is not null : was != now)
            {
                lines.Add(new ProtonChange(label, before is null ? null : was ?? "none", now ?? "none"));
            }
        }

        Line("Title", before?.Title, after.Title);
        Line("Type", before?.Type, after.Type);
        Line("Status", Status(before?.Status), Status(after.Status));
        Line("Year", before?.Year?.ToString(CultureInfo.InvariantCulture), after.Year?.ToString(CultureInfo.InvariantCulture));
        Line("Client", before?.Client, after.Client);
        Line("Place", Place(before), Place(after));
        Line("Description", ProtonText.Cut(before?.Description, 200), ProtonText.Cut(after.Description, 200));
        Line("On the website", before is null ? null : before.Published ? "published" : "draft", after.Published ? "published" : "draft");
        return lines;
    }

    private static string? Status(WebsiteProjectStatus? status) => status switch
    {
        WebsiteProjectStatus.InProgress => "in progress",
        WebsiteProjectStatus.Completed => "completed",
        _ => null,
    };

    private static string? Place(WebsiteProjectRequest? request)
        => request is null ? null : string.Join(", ", new[] { request.City, request.Country }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } place ? place : null;
}
