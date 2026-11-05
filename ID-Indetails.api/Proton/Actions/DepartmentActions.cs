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
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The departments when proposed: if they changed since, the change is refused, not applied over theirs.</param>
public sealed record ProjectDepartmentsArgs(long ProjectId, SetProjectDepartmentsRequest Request, IReadOnlyList<long> Before);

public sealed record ProjectDepartmentsInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("Every department that should work on the project afterwards (team ids of Design departments, from find), " +
                 "the ones already on it included. Adding or taking one off puts the shares back to equal.")]
    public required IReadOnlyList<long> DepartmentIds { get; init; }
}

/// <summary>
/// Chooses the departments of a Design client project, through PUT /api/projects/{id}/departments once confirmed. What
/// the API refuses is refused here first: an Execution or internal project, a finalised one, a department that isn't
/// Design's, and taking off one with hours.
/// </summary>
public sealed class ProposeProjectDepartmentsTool(
    IProjectService projects, IProjectDepartmentService projectDepartments, IProjectDepartmentRepository departments, IDesignVoRepository designVos)
    : ProtonActionTool<ProjectDepartmentsInput, ProjectDepartmentsArgs>
{
    public override string Name => "propose_project_departments";

    public override string Description =>
        "Propose choosing which departments work on a Design client project: pass all of them, the current ones included. " +
        "The shares go back to equal; hours people logged while their department was missing then go through. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectDepartmentsController>(nameof(ProjectDepartmentsController.Set));

    protected override async Task<(ProjectDepartmentsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProjectDepartmentsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        DepartmentText.CheckProject(project);

        var chosen = input.DepartmentIds.ToList();
        if (chosen.Count == 0)
        {
            throw new ProtonToolInputException("Which departments work on this project? Pass all of them.");
        }

        var found = await departments.CandidatesAsync(chosen, cancellationToken);
        ProjectDepartmentRules.CheckChosen(project.Domain, chosen, found).ThrowIfDenied();

        var current = (await projectDepartments.GetAsync(project.Id, cancellationToken)).Departments;
        var currentIds = current.Select(d => d.Id).ToList();
        if (currentIds.ToHashSet().SetEquals(chosen))
        {
            throw new ProtonToolInputException("These are already the project's departments: say which should be added or taken off.");
        }

        var removed = current.Where(d => !chosen.Contains(d.Id)).ToList();
        var withHours = (await departments.WithHoursAsync(project.Id, cancellationToken))
            .Concat((await designVos.AppliedAsync(project.Id, cancellationToken)).ByDepartment.Keys)
            .ToHashSet();
        ProjectDepartmentRules.CheckRemoval(removed.Where(d => withHours.Contains(d.Id)).Select(d => d.Name).ToList()).ThrowIfDenied();

        var added = found.Where(f => !currentIds.Contains(f.Id)).Select(f => f.Name).Order().ToList();
        var changes = new List<ProtonChange>();
        if (added.Count > 0)
        {
            changes.Add(new ProtonChange("Added", null, string.Join(", ", added)));
        }

        if (removed.Count > 0)
        {
            changes.Add(new ProtonChange("Taken off", string.Join(", ", removed.Select(d => d.Name).Order()), "no longer a department"));
        }

        changes.Add(new ProtonChange("Shares", "as they are", "equal again"));

        var note = "The shares go back to equal." + (added.Count > 0
            ? " Hours people logged while their department was missing, and that wait for an added one, go through."
            : "");
        return (new ProjectDepartmentsArgs(project.Id, new SetProjectDepartmentsRequest { DepartmentIds = chosen }, currentIds),
            new ProtonPreview($"Change the departments of {project.Title}", changes, note),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(ProjectDepartmentsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectDepartmentsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = (await projectDepartments.GetAsync(args.ProjectId, cancellationToken)).Departments.Select(d => d.Id).ToHashSet();
        if (!current.SetEquals(args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The project's departments changed since Proton proposed this. Ask again.");
        }

        var project = await projects.GetAsync(args.ProjectId, cancellationToken);
        await projectDepartments.SetAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Changed the departments of {project.Title}.", "project", project.Id, [ProjectText.Ref(project)]);
    }
}

/// <param name="Before">The shares when proposed: if they changed since, the change is refused.</param>
public sealed record DepartmentSharesArgs(long ProjectId, SetDepartmentSharesRequest Request, IReadOnlyList<DepartmentShareRequest> Before);

public sealed record DepartmentSharePercent(
    [property: Description("The department's id (a team id on the project, from get_project_departments).")] long DepartmentId,
    [property: Description("Its share in percent as the user said it: 70 means 70%. Up to two decimals.")] decimal Percent);

public sealed record DepartmentSharesInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("A share for each of the project's departments, and only them, adding up to 100.")]
    public required IReadOnlyList<DepartmentSharePercent> Shares { get; init; }

    [Description("Why the split changes, in the user's words. Optional.")]
    public string? Reason { get; init; }
}

/// <summary>
/// Sets each department's share of a Design project's price, through PUT /api/projects/{id}/departments/shares once
/// confirmed. The percentages must be the user's own words, and the project's name is typed to confirm: the shares move
/// every department's budget.
/// </summary>
public sealed class ProposeDepartmentSharesTool(IProjectService projects, IProjectDepartmentService projectDepartments)
    : ProtonActionTool<DepartmentSharesInput, DepartmentSharesArgs>
{
    public override string Name => "propose_department_shares";

    public override string Description =>
        "Propose how a Design client project's price is split between its departments, in percent adding up to 100. The " +
        "percentages must be ones the user typed, not worked out. It changes every department's budget. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectDepartmentsController>(nameof(ProjectDepartmentsController.SetShares));

    protected override async Task<(DepartmentSharesArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DepartmentSharesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        DepartmentText.CheckProject(project);

        var current = (await projectDepartments.GetAsync(project.Id, cancellationToken)).Departments;
        if (current.Count == 0)
        {
            throw new ProtonToolInputException("This project has no departments yet: choose them first.");
        }

        MoneyText.RequireTyped(context, [.. input.Shares.Select(s => (decimal?)s.Percent)]);
        var shares = input.Shares.Select(s => new DepartmentShareRequest(s.DepartmentId, s.Percent / 100m)).ToList();
        ProjectDepartmentRules.CheckShares(current.Select(d => d.Id).ToList(), shares.Select(s => (s.DepartmentId, s.Share)).ToList()).ThrowIfDenied();

        var before = current.ToDictionary(d => d.Id, d => d.Share);
        if (current.All(d => before[d.Id] == shares.Single(s => s.DepartmentId == d.Id).Share))
        {
            throw new ProtonToolInputException("The shares are already these: say what should change.");
        }

        var changes = current
            .Select(d => new ProtonChange(d.Name, before[d.Id] is { } was ? Percent(was) : null, Percent(shares.Single(s => s.DepartmentId == d.Id).Share)))
            .ToList();
        var reason = MoneyText.Clean(input.Reason);
        if (reason is not null)
        {
            changes.Add(new ProtonChange("Reason", null, reason));
        }

        var request = new SetDepartmentSharesRequest { Shares = shares, Reason = reason };
        return (new DepartmentSharesArgs(project.Id, request, current.Select(d => new DepartmentShareRequest(d.Id, before[d.Id] ?? 0m)).ToList()),
            new ProtonPreview($"Split {project.Title} between its departments", changes,
                "Each department's budget follows its share of the price. The change is kept in the project's history.",
                Confirm: ProtonConfirm.Typed, Phrase: project.Title),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(DepartmentSharesArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(DepartmentSharesArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = (await projectDepartments.GetAsync(args.ProjectId, cancellationToken)).Departments;
        if (current.Count != args.Before.Count
            || current.Any(d => args.Before.SingleOrDefault(b => b.DepartmentId == d.Id) is not { } was || (d.Share is { } now && now != was.Share)))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The project's departments or shares changed since Proton proposed this. Ask again.");
        }

        var project = await projects.GetAsync(args.ProjectId, cancellationToken);
        await projectDepartments.SetSharesAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Changed the split of {project.Title}.", "project", project.Id, [ProjectText.Ref(project)]);
    }

    private static string Percent(decimal share) => $"{(share * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%";
}

internal static class DepartmentText
{
    /// <summary>What the API refuses for a project before looking at the departments: not Design client work, or finalised.</summary>
    public static void CheckProject(ProjectResponse project)
    {
        if (project.Domain != DomainCodes.Design)
        {
            throw new ProtonToolInputException("Only a Design project has departments.");
        }

        if (project.BillingType != BillingType.Client)
        {
            throw new ProtonToolInputException("An internal project has no price to split, so it has no departments.");
        }

        ProjectRules.CheckMoneyEditable(project.Status is ProjectStatus.Completed or ProjectStatus.Archived).ThrowIfDenied();
    }
}
