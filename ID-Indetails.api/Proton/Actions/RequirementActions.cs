using System.ComponentModel;
using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The requirement as it was when proposed: if it changed since, the update is refused, not applied over it.</param>
public sealed record RequirementArgs(long ProjectId, long? RequirementId, SaveRequirementRequest Request, SaveRequirementRequest? Before);

public sealed record RequirementDoneArgs(long ProjectId, long RequirementId, bool Done);

public sealed record AddRequirementInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("What has to be done, as the user said it.")]
    public required string Text { get; init; }

    public DateOnly? Deadline { get; init; }

    [Description("Employees to assign (ids from find). Only employees of the project's domain can be assigned.")]
    public IReadOnlyList<long>? AssigneeIds { get; init; }
}

/// <summary>Adds a requirement, through POST /api/projects/{id}/requirements once confirmed.</summary>
public sealed class ProposeAddRequirementTool(IProjectService projects, IEmployeeService employees, IRequirementService requirements)
    : ProtonActionTool<AddRequirementInput, RequirementArgs>
{
    public override string Name => "propose_add_requirement";

    public override string Description =>
        "Propose adding a requirement (task) to a project. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(RequirementArgs args, ProtonRef? target) => RequirementText.OpensLabel(target);

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<RequirementsController>(nameof(RequirementsController.Create));

    protected override async Task<(RequirementArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AddRequirementInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        RequirementRules.CheckEditable(project.Status).ThrowIfDenied();

        var request = new SaveRequirementRequest { Text = input.Text.Trim(), Deadline = input.Deadline, AssigneeIds = input.AssigneeIds ?? [] };
        var preview = new ProtonPreview(
            $"Add a requirement to {project.Title}",
            [
                new ProtonChange("Requirement", null, request.Text),
                new ProtonChange("Deadline", null, RequirementText.Deadline(request.Deadline)),
                new ProtonChange("Assigned to", null, await RequirementText.PeopleAsync(employees, request.AssigneeIds, cancellationToken)),
            ]);
        return (new RequirementArgs(project.Id, null, request, null), preview, new ProtonRef(ProtonRefType.Project, project.Id, project.Title, project.Domain));
    }

    protected override object RequestOf(RequirementArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(RequirementArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var created = await requirements.CreateAsync(args.ProjectId, args.Request, cancellationToken);
        return await RequirementText.DoneAsync(projects, args.ProjectId, $"Added the requirement \"{ProtonText.Cut(created.Text, 80)}\".", created.Id, cancellationToken);
    }
}

public sealed record UpdateRequirementInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    public required long RequirementId { get; init; }

    [Description("The new text, if it changes.")]
    public string? Text { get; init; }

    [Description("The new deadline, if it changes.")]
    public DateOnly? Deadline { get; init; }

    [Description("Remove the deadline.")]
    public bool ClearDeadline { get; init; }

    [Description("The complete new list of assignees, if it changes (empty to unassign everyone).")]
    public IReadOnlyList<long>? AssigneeIds { get; init; }
}

/// <summary>Edits a requirement, through PUT /api/projects/{id}/requirements/{requirementId} once confirmed.</summary>
public sealed class ProposeUpdateRequirementTool(IProjectService projects, IEmployeeService employees, IRequirementService requirements)
    : ProtonActionTool<UpdateRequirementInput, RequirementArgs>
{
    public override string Name => "propose_update_requirement";

    public override string Description =>
        "Propose changing a requirement's text, deadline or assignees (use get_project_requirements for its id). " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(RequirementArgs args, ProtonRef? target) => RequirementText.OpensLabel(target);

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<RequirementsController>(nameof(RequirementsController.Update));

    protected override async Task<(RequirementArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateRequirementInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await RequirementText.FindAsync(requirements, input.ProjectId, input.RequirementId, cancellationToken);
        var before = RequirementText.RequestOf(current);
        var after = new SaveRequirementRequest
        {
            Text = input.Text?.Trim() ?? before.Text,
            Deadline = input.ClearDeadline ? null : input.Deadline ?? before.Deadline,
            AssigneeIds = input.AssigneeIds ?? before.AssigneeIds,
        };

        var changes = new List<ProtonChange>();
        if (after.Text != before.Text)
        {
            changes.Add(new ProtonChange("Requirement", before.Text, after.Text));
        }

        if (after.Deadline != before.Deadline)
        {
            changes.Add(new ProtonChange("Deadline", RequirementText.Deadline(before.Deadline), RequirementText.Deadline(after.Deadline)));
        }

        if (!after.AssigneeIds.Order().SequenceEqual(before.AssigneeIds.Order()))
        {
            changes.Add(new ProtonChange(
                "Assigned to",
                RequirementText.Names(current.Assignees.Select(a => a.Name)),
                await RequirementText.PeopleAsync(employees, after.AssigneeIds, cancellationToken)));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new RequirementArgs(input.ProjectId, current.Id, after, before),
            new ProtonPreview($"Change the requirement \"{ProtonText.Cut(current.Text, 60)}\"", changes), await RequirementText.ProjectAsync(projects, input.ProjectId, cancellationToken));
    }

    protected override object RequestOf(RequirementArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(RequirementArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await RequirementText.FindAsync(requirements, args.ProjectId, args.RequirementId!.Value, cancellationToken);
        if (!RequirementText.Same(RequirementText.RequestOf(current), args.Before!))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The requirement changed since Proton proposed this. Ask again.");
        }

        var updated = await requirements.UpdateAsync(args.ProjectId, current.Id, args.Request, cancellationToken);
        return await RequirementText.DoneAsync(projects, args.ProjectId, $"Updated the requirement \"{ProtonText.Cut(updated.Text, 80)}\".", updated.Id, cancellationToken);
    }
}

public sealed record SetRequirementDoneInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    public required long RequirementId { get; init; }

    [Description("True to tick it as done, false to reopen it.")]
    public required bool Done { get; init; }
}

/// <summary>Ticks or unticks a requirement, through POST …/requirements/{id}/done or /undone once confirmed.</summary>
public sealed class ProposeSetRequirementDoneTool(IProjectService projects, IRequirementService requirements)
    : ProtonActionTool<SetRequirementDoneInput, RequirementDoneArgs>
{
    public override string Name => "propose_set_requirement_done";

    public override string Description =>
        "Propose ticking a requirement as done, or reopening a done one. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(RequirementDoneArgs args, ProtonRef? target) => RequirementText.OpensLabel(target);

    // Done and Undone share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<RequirementsController>(nameof(RequirementsController.Done));

    protected override async Task<(RequirementDoneArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SetRequirementDoneInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await RequirementText.FindAsync(requirements, input.ProjectId, input.RequirementId, cancellationToken);
        if (current.IsDone == input.Done)
        {
            throw new ProtonToolInputException(current.IsDone ? "It is already done." : "It is already open.");
        }

        return (new RequirementDoneArgs(input.ProjectId, current.Id, input.Done),
            new ProtonPreview(
                input.Done ? "Mark a requirement done" : "Reopen a requirement",
                [
                    new ProtonChange("Requirement", null, ProtonText.Cut(current.Text, 200)!),
                    new ProtonChange("State", current.IsDone ? "Done" : "Open", input.Done ? "Done" : "Open"),
                ]),
            await RequirementText.ProjectAsync(projects, input.ProjectId, cancellationToken));
    }

    protected override object? RequestOf(RequirementDoneArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(RequirementDoneArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var result = await requirements.SetDoneAsync(args.ProjectId, args.RequirementId, args.Done, cancellationToken);
        return await RequirementText.DoneAsync(
            projects, args.ProjectId, result.IsDone ? $"Marked \"{ProtonText.Cut(result.Text, 80)}\" done." : $"Reopened \"{ProtonText.Cut(result.Text, 80)}\".", result.Id, cancellationToken);
    }
}

/// <summary>Shared wording and lookups of the requirement actions.</summary>
internal static class RequirementText
{
    /// <summary>The project a requirement belongs to, as a record (read with the user's access).</summary>
    public static async Task<ProtonRef> ProjectAsync(IProjectService projects, long projectId, CancellationToken cancellationToken)
        => ProjectText.Ref(await projects.GetAsync(projectId, cancellationToken));

    /// <summary>A requirement change done: it lives on its project's requirements tab.</summary>
    public static async Task<ProtonActionDone> DoneAsync(
        IProjectService projects, long projectId, string message, long requirementId, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(projects, projectId, cancellationToken);
        return new ProtonActionDone(message, "requirement", requirementId, [project], ProtonUiCommand.Open(project, ProtonProjectSection.Requirements));
    }

    public static string? OpensLabel(ProtonRef? project) => project is null ? null : $"{project.Name}: requirements";

    public static async Task<RequirementResponse> FindAsync(
        IRequirementService requirements, long projectId, long requirementId, CancellationToken cancellationToken)
        => (await requirements.ListAsync(projectId, cancellationToken)).Items.FirstOrDefault(r => r.Id == requirementId)
            ?? throw new NotFoundException("Requirement not found.");

    public static SaveRequirementRequest RequestOf(RequirementResponse r)
        => new() { Text = r.Text, Deadline = r.Deadline, AssigneeIds = r.Assignees.Select(a => a.Id).ToList() };

    public static bool Same(SaveRequirementRequest a, SaveRequirementRequest b)
        => a.Text == b.Text && a.Deadline == b.Deadline && a.AssigneeIds.Order().SequenceEqual(b.AssigneeIds.Order());

    public static string Deadline(DateOnly? deadline) => deadline?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    public static string Names(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "nobody" : string.Join(", ", list);
    }

    /// <summary>Names for the card, through the employee endpoint's service: someone out of the user's reach reads as not found.</summary>
    public static async Task<string> PeopleAsync(IEmployeeService employees, IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (var id in ids.Distinct())
        {
            var person = await employees.GetAsync(id, cancellationToken);
            names.Add($"{person.FirstName} {person.LastName}".Trim());
        }

        return Names(names);
    }
}
