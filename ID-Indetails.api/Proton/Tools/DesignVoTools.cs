using System.ComponentModel;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record DesignVoProjectInput
{
    [Description("A Design client project: an id from find or another tool.")]
    public required long ProjectId { get; init; }
}

public sealed record DesignVoLineRow(string Description, decimal Amount);

public sealed record DesignVoDepartmentRow(long DepartmentId, string Department, decimal Amount);

/// <param name="TotalUsd">Before TVA; below zero for less work.</param>
public sealed record DesignVoRow(
    long Id,
    string Number,
    string Title,
    DesignVoStatus Status,
    DesignVoSplit Split,
    decimal TotalUsd,
    decimal TvaUsd,
    decimal TotalWithTvaUsd,
    IReadOnlyList<DesignVoLineRow> Lines,
    IReadOnlyList<DesignVoDepartmentRow> Departments,
    int? OutsideScopeMinutes,
    DateOnly? OutsideScopeFrom,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? AppliedAt,
    DateTime? CancelledAt)
{
    public static DesignVoRow Of(DesignVoResponse v) => new(
        v.Id, v.Number, ProtonText.Cut(v.Title, 200)!, v.Status, v.Split, v.TotalUsd, v.TvaUsd, v.TotalWithTvaUsd,
        v.Lines.Select(l => new DesignVoLineRow(ProtonText.Cut(l.Description, 300)!, l.Amount)).ToList(),
        v.Allocations.Select(a => new DesignVoDepartmentRow(a.DepartmentId, a.Name, a.Amount)).ToList(),
        v.OutsideScopeMinutes, v.OutsideScopeFrom, v.CreatedBy.Name, v.CreatedAt, v.SentAt, v.AppliedAt, v.CancelledAt);
}

/// <param name="Share">A fraction (0.25 is 25%): what a VO split by the shares gives the department.</param>
public sealed record DesignVoDepartment(long DepartmentId, string Name, decimal? Share);

/// <param name="PriceUsd">The project's agreed price before TVA: the customer payment plus the applied VOs.</param>
/// <param name="OutsideScopeMinutes">Hours logged outside the agreed scope since <paramref name="OutsideScopeFrom"/>: what a new VO can show.</param>
public sealed record DesignVoList(
    decimal PriceUsd,
    decimal AppliedUsd,
    decimal PendingUsd,
    DateOnly OutsideScopeFrom,
    int OutsideScopeMinutes,
    IReadOnlyList<DesignVoDepartment> Departments,
    IReadOnlyList<DesignVoRow> Vos,
    string Note);

/// <summary>
/// A Design client project's variation orders and the hours outside the agreed scope, as GET /api/projects/{id}/design-vos and
/// /design-vos/outside-scope return them: for those who see the money.
/// </summary>
public sealed class ListDesignVosTool(IDesignVoService vos, IProjectService projects, IProjectDepartmentService departments) : ProtonTool<DesignVoProjectInput>
{
    public override string Name => "list_design_vos";

    public override string Description =>
        "A Design client project's variation orders (VOs), newest first: number, title, status (pending, applied or cancelled), lines, total " +
        "before and with TVA, how it is split between departments, and when it was sent, applied or cancelled; the project's price and " +
        "what the VOs make of it; the hours logged outside the agreed scope since the last applied VO; and the project's departments " +
        "with their shares. Their ids are what propose_design_vo needs. Execution sites have BOQ variation orders (list_boqs).";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<DesignVosController>(nameof(DesignVosController.List));

    protected override async Task<ProtonToolOutput> RunAsync(DesignVoProjectInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var list = await vos.ListAsync(input.ProjectId, cancellationToken);
        var outside = await vos.OutsideScopeAsync(input.ProjectId, cancellationToken);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var shares = await departments.GetAsync(input.ProjectId, cancellationToken);
        var rows = list.Select(DesignVoRow.Of).ToList();
        return ProtonToolOutput.Of(
            new DesignVoList(
                project.Money?.Revenue ?? 0,
                list.Where(v => v.Status == DesignVoStatus.Applied).Sum(v => v.TotalUsd),
                list.Where(v => v.Status == DesignVoStatus.Pending).Sum(v => v.TotalUsd),
                outside.From, outside.Minutes,
                shares.Departments.Select(d => new DesignVoDepartment(d.Id, d.Name, d.Share)).ToList(),
                rows,
                "Amounts are in USD before TVA. Only applied VOs are part of the price; a pending one waits for the client to confirm."),
            new ProtonRef(ProtonRefType.Project, project.Id, project.Title, project.Domain));
    }
}
