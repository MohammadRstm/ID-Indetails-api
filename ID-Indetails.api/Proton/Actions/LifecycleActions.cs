using System.ComponentModel;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>The status changes that lock or unlock a project's revenue.</summary>
public enum ProtonLifecycleChange
{
    Complete,
    Archive,
    Reopen,
}

public sealed record ProjectLifecycleInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("complete finishes an active or held project; archive puts any project away; reopen makes a completed or archived one active again.")]
    public required ProtonLifecycleChange Change { get; init; }
}

/// <summary>
/// Completes, archives or reopens a project, through POST /api/projects/{id}/complete, /archive or /reopen once
/// confirmed. Completing and archiving lock its revenue and tick its open requirements; reopening unlocks them. The user
/// types the project's name to confirm.
/// </summary>
public sealed class ProposeProjectLifecycleTool(IProjectService projects, IProjectStatusService statuses, IRequirementService requirements)
    : ProtonActionTool<ProjectLifecycleInput, ProjectStatusArgs>
{
    public override string Name => "propose_project_lifecycle";

    public override string Description =>
        "Propose completing, archiving or reopening a project. Completing or archiving locks its revenue and ticks its " +
        "open requirements; reopening unlocks them. The user confirms by typing the project's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // Complete, Archive and Reopen share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Complete));

    protected override async Task<(ProjectStatusArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProjectLifecycleInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var action = input.Change switch
        {
            ProtonLifecycleChange.Complete => ProjectStatusRules.Action.Complete,
            ProtonLifecycleChange.Archive => ProjectStatusRules.Action.Archive,
            _ => ProjectStatusRules.Action.Reopen,
        };
        var (transition, denial) = ProjectStatusRules.Plan(project.Status, action);
        denial.ThrowIfDenied();

        string effect;
        if (transition!.Finalises)
        {
            var open = (await requirements.ListAsync(project.Id, cancellationToken)).Items.Count(r => !r.IsDone);
            effect = $"Its revenue is locked{(open > 0 ? $", and its {open} open requirement(s) are ticked done" : "")}.";
        }
        else if (transition.Reopens)
        {
            effect = "Its revenue is unlocked, and the requirements completing ticked are open again.";
        }
        else
        {
            effect = "It leaves the active lists.";
        }

        var title = input.Change switch
        {
            ProtonLifecycleChange.Complete => $"Complete {project.Title}",
            ProtonLifecycleChange.Archive => $"Archive {project.Title}",
            _ => $"Reopen {project.Title}",
        };
        return (new ProjectStatusArgs(project.Id, action),
            new ProtonPreview(title,
                [new ProtonChange("Status", ProjectText.Status(project.Status), ProjectText.Status(transition.To))],
                $"{effect} The project's domain is notified.",
                Confirm: ProtonConfirm.Typed,
                Phrase: project.Title),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(ProjectStatusArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectStatusArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await statuses.ChangeAsync(args.ProjectId, args.Action, cancellationToken);
        return new ProtonActionDone($"{project.Title} is now {ProjectText.Status(project.Status).ToLowerInvariant()}.",
            "project", project.Id, [ProjectText.Ref(project)], ProtonUiCommand.Open(ProjectText.Ref(project)));
    }
}
