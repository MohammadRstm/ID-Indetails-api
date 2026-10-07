using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>The status changes Proton may propose. Completing, archiving and reopening lock or unlock revenue, so they stay in the app.</summary>
public enum ProtonStatusChange
{
    Hold,
    Resume,
}

public sealed record ProjectStatusArgs(long ProjectId, ProjectStatusRules.Action Action);

public sealed record ChangeProjectStatusInput
{
    public required long ProjectId { get; init; }

    [Description("hold puts an active project on hold; resume makes a held project active again.")]
    public required ProtonStatusChange Change { get; init; }
}

/// <summary>Puts a project on hold or resumes it, through POST /api/projects/{id}/hold or /resume once confirmed.</summary>
public sealed class ProposeChangeProjectStatusTool(IProjectService projects, IProjectStatusService statuses)
    : ProtonActionTool<ChangeProjectStatusInput, ProjectStatusArgs>
{
    public override string Name => "propose_change_project_status";

    public override string Description =>
        "Propose putting a project on hold or resuming it. Completing, archiving and reopening are done in the app. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // Hold and Resume share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Hold));

    protected override async Task<(ProjectStatusArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ChangeProjectStatusInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var action = input.Change == ProtonStatusChange.Hold ? ProjectStatusRules.Action.Hold : ProjectStatusRules.Action.Resume;
        var (transition, denial) = ProjectStatusRules.Plan(project.Status, action);
        denial.ThrowIfDenied();

        return (new ProjectStatusArgs(project.Id, action),
            new ProtonPreview(
                action == ProjectStatusRules.Action.Hold ? $"Put {project.Title} on hold" : $"Resume {project.Title}",
                [new ProtonChange("Status", ProjectText.Status(project.Status), ProjectText.Status(transition!.To))],
                "The project's domain is notified, as when the change is made in the app."),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(ProjectStatusArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectStatusArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await statuses.ChangeAsync(args.ProjectId, args.Action, cancellationToken);
        return new ProtonActionDone($"{project.Title} is now {ProjectText.Status(project.Status).ToLowerInvariant()}.",
            "project", project.Id, [ProjectText.Ref(project)]);
    }
}

/// <param name="Before">The details when proposed: if someone changed them since, the update is refused, not applied over theirs.</param>
public sealed record UpdateProjectArgs(long ProjectId, UpdateProjectRequest Request, UpdateProjectRequest Before);

public sealed record UpdateProjectInput
{
    public required long ProjectId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public DateOnly? TargetDate { get; init; }

    [Description("Remove the target date.")]
    public bool ClearTargetDate { get; init; }

    [Description("The new owner (an admin of the project's domain, or a super admin; id from find).")]
    public long? OwnerId { get; init; }
}

/// <summary>Changes a project's details or owner, through PUT /api/projects/{id} once confirmed. Money is not part of it.</summary>
public sealed class ProposeUpdateProjectTool(IProjectService projects, IEmployeeService employees)
    : ProtonActionTool<UpdateProjectInput, UpdateProjectArgs>
{
    public override string Name => "propose_update_project";

    public override string Description =>
        "Propose changing a project's title, description, location, target date or owner. Revenue, client and pricing " +
        "are changed in the app. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Update));

    protected override async Task<(UpdateProjectArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateProjectInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var before = ProjectText.RequestOf(project);
        var after = before with
        {
            Title = input.Title?.Trim() ?? before.Title,
            Description = input.Description ?? before.Description,
            Location = input.Location ?? before.Location,
            TargetDate = input.ClearTargetDate ? null : input.TargetDate ?? before.TargetDate,
            OwnerId = input.OwnerId ?? before.OwnerId,
        };

        var changes = new List<ProtonChange>();
        Add(changes, "Title", before.Title, after.Title);
        Add(changes, "Description", ProtonText.Cut(before.Description, 200), ProtonText.Cut(after.Description, 200));
        Add(changes, "Location", before.Location, after.Location);
        Add(changes, "Target date", ProjectText.Date(before.TargetDate), ProjectText.Date(after.TargetDate));
        if (after.OwnerId != before.OwnerId)
        {
            var owner = await employees.GetAsync(after.OwnerId, cancellationToken);
            changes.Add(new ProtonChange("Owner", project.Owner.Name, $"{owner.FirstName} {owner.LastName}".Trim()));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new UpdateProjectArgs(project.Id, after, before),
            new ProtonPreview($"Change {project.Title}", changes, after.OwnerId != before.OwnerId ? "The new owner is notified." : null),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(UpdateProjectArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(UpdateProjectArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await projects.GetAsync(args.ProjectId, cancellationToken);
        if (ProjectText.RequestOf(current) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The project changed since Proton proposed this. Ask again.");
        }

        var project = await projects.UpdateAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Updated {project.Title}.", "project", project.Id, [ProjectText.Ref(project)]);
    }

    private static void Add(List<ProtonChange> changes, string label, string? before, string? after)
    {
        if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal))
        {
            changes.Add(new ProtonChange(label, string.IsNullOrEmpty(before) ? "none" : before, string.IsNullOrEmpty(after) ? "none" : after));
        }
    }
}

public sealed record CreateProjectArgs(CreateProjectRequest Request);

public sealed record CreateProjectInput
{
    public required string Title { get; init; }

    [Description("The client it is for (id from find). Required unless it is internal.")]
    public long? ClientId { get; init; }

    [Description("An internal project, with no client and no revenue.")]
    public bool Internal { get; init; }

    [Description("The owner (an admin of Design or a super admin). Leave out to make the user the owner.")]
    public long? OwnerId { get; init; }

    public DateOnly? TargetDate { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }
}

/// <summary>
/// Creates a Design project, through POST /api/projects once confirmed. Without money: revenue and Execution sites
/// (which need a fee and markup) are set up in the app.
/// </summary>
public sealed class ProposeCreateProjectTool(IProjectService projects, IClientService clients, IEmployeeService employees)
    : ProtonActionTool<CreateProjectInput, CreateProjectArgs>
{
    public override string Name => "propose_create_project";

    public override string Description =>
        "Propose creating a Design project for a client (or an internal one). Revenue isn't set here, and Execution sites " +
        "are created in the app. Ask the user for the client if they didn't say. Nothing changes until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Create));

    protected override async Task<(CreateProjectArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateProjectInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        string clientName;
        if (input.Internal)
        {
            if (input.ClientId is not null)
            {
                throw new ProtonToolInputException("An internal project has no client: drop the client or make it a client project.");
            }

            clientName = "none (internal)";
        }
        else
        {
            var clientId = input.ClientId ?? throw new ProtonToolInputException("Which client is this project for? Or is it internal?");
            var client = await clients.GetAsync(clientId, cancellationToken);
            if (client.Domain != DomainCodes.Design)
            {
                throw new ProtonToolInputException($"{client.DisplayName} is an Execution client; Proton creates Design projects only.");
            }

            ProjectRules.CheckClient(client.IsArchived).ThrowIfDenied();
            clientName = client.DisplayName;
        }

        var ownerName = "you";
        if (input.OwnerId is { } ownerId)
        {
            var owner = await employees.GetAsync(ownerId, cancellationToken);
            ownerName = $"{owner.FirstName} {owner.LastName}".Trim();
        }

        var request = new CreateProjectRequest
        {
            Title = input.Title.Trim(),
            Description = input.Description,
            Location = input.Location,
            DomainCode = DomainCodes.Design,
            BillingType = input.Internal ? BillingType.Internal : BillingType.Client,
            ClientId = input.ClientId,
            OwnerId = input.OwnerId,
            TargetDate = input.TargetDate,
        };

        var sameName = await projects.ListAsync(new ProjectListQuery { Search = request.Title, Domain = DomainCodes.Design, PageSize = 25 }, cancellationToken);
        var duplicate = sameName.Items.Any(p => p.Title.Equals(request.Title, StringComparison.OrdinalIgnoreCase));

        var changes = new List<ProtonChange>
        {
            new("Name", null, request.Title),
            new("Client", null, clientName),
            new("Owner", null, ownerName),
            new("Status", null, "Active"),
            new("Start date", null, ProjectText.Date(context.Today)),
            new("Target date", null, ProjectText.Date(request.TargetDate)),
        };
        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            changes.Add(new ProtonChange("Location", null, request.Location));
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            changes.Add(new ProtonChange("Description", null, ProtonText.Cut(request.Description, 200)!));
        }

        var note = (duplicate ? $"A Design project called \"{request.Title}\" already exists. " : "")
            + (input.Internal ? "" : "Revenue isn't set by Proton: add it on the project page.");
        return (new CreateProjectArgs(request), new ProtonPreview("Create a Design project", changes, note.Length > 0 ? note.Trim() : null), null);
    }

    protected override object RequestOf(CreateProjectArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CreateProjectArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.CreateAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Created the project {project.Title}.", "project", project.Id, [ProjectText.Ref(project)]);
    }
}

internal static class ProjectText
{
    public static ProtonRef Ref(ProjectResponse p) => new(ProtonRefType.Project, p.Id, p.Title, p.Domain);

    public static UpdateProjectRequest RequestOf(ProjectResponse p) => new()
    {
        Title = p.Title,
        Description = p.Description,
        Location = p.Location,
        Icon = p.Icon,
        Tint = p.Tint,
        TargetDate = p.TargetDate,
        OwnerId = p.Owner.Id,
    };

    public static string Status(ProjectStatus status) => status == ProjectStatus.Hold ? "On hold" : status.ToString();

    public static string Date(DateOnly? day) => day?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "none";
}
