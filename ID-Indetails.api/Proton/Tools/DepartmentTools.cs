using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>Who logged hours in a department: their minutes only. A person's cost beside their minutes is their hourly cost, which never reaches Proton.</summary>
public sealed record DepartmentPersonRow(string Person, int Minutes);

/// <param name="SharePercent">Its part of the price. Null without financial:view, like all the money here.</param>
/// <param name="Left">Budget less cost; below zero when over budget.</param>
public sealed record DepartmentRow(
    long Id, string Name, decimal? SharePercent, decimal? Budget, decimal? Cost, decimal? Left, int? MinutesLogged, IReadOnlyList<DepartmentPersonRow>? People);

public sealed record DepartmentChangeRow(
    DateTime At, string ChangedBy, string Kind, string Before, string After, string? Reason);

/// <param name="Other">Hours by people in no department, or in one no longer on the project: they belong to no budget.</param>
/// <param name="History">Newest first; only for someone who may read it.</param>
public sealed record ProjectDepartmentsCard(
    long ProjectId, string? FullFile, IReadOnlyList<DepartmentRow> Departments, DepartmentRow? Other, IReadOnlyList<DepartmentChangeRow>? History);

/// <summary>
/// A Design project's departments, as GET /api/projects/{id}/departments shows them to this caller: who works on it
/// and, with financial:view, each one's share, budget, cost, hours and people. The history of the split only for whoever
/// may read it.
/// </summary>
public sealed class GetProjectDepartmentsTool(IProjectDepartmentService departments) : ProtonTool<ProjectIdInput>
{
    private const int MaxHistory = 10;

    private static readonly ProtonEndpoint HistoryEndpoint = ProtonEndpoint.Of<ProjectDepartmentsController>(nameof(ProjectDepartmentsController.History));

    public override string Name => "get_project_departments";

    public override string Description =>
        "A Design project's departments: which work on it, and, for someone who sees the money, each one's share of the price, " +
        "budget, cost so far, minutes logged, what is left and who logged how many minutes (never a person's cost), plus " +
        "hours that belong to no budget and the history of the split. Without money access only the department names. " +
        "Execution sites have no departments.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectDepartmentsController>(nameof(ProjectDepartmentsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var response = await departments.GetAsync(input.ProjectId, cancellationToken);
        IReadOnlyList<DepartmentChangeRow>? history = null;
        if (await context.CanCallAsync(HistoryEndpoint))
        {
            history = (await departments.HistoryAsync(input.ProjectId, cancellationToken))
                .Take(MaxHistory)
                .Select(c => new DepartmentChangeRow(
                    c.ChangedAt, c.ChangedBy.Name, c.Kind.ToString(), Split(c.Before), Split(c.After), c.Reason))
                .ToList();
        }

        var card = new ProjectDepartmentsCard(
            input.ProjectId, response.Name, response.Departments.Select(Row).ToList(),
            response.Other is { } other ? Row(0, "Other", null, other) : null, history);
        return new ProtonToolOutput(card, []);
    }

    private static DepartmentRow Row(ProjectDepartmentResponse d) => Row(d.Id, d.Name, d.Share, d.Money);

    private static DepartmentRow Row(long id, string name, decimal? share, DepartmentMoneyResponse? money)
        => new(id, name, share is { } s ? s * 100m : null, money?.Budget, money?.Cost, money?.Left, money?.Minutes,
            money?.People.Select(p => new DepartmentPersonRow(p.Person.Name, p.Minutes)).ToList());

    private static string Split(IReadOnlyList<DepartmentShareResponse> split)
        => split.Count == 0 ? "none" : string.Join(", ", split.Select(s => $"{s.Name} {(s.Share * 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%"));
}
