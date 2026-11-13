using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Reports.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record UtilizationInput
{
    [Description("Any day of the month to show; this month when left out. Never a future month.")]
    public DateOnly? Month { get; init; }
}

/// <summary>Hours only: never pay, a shortfall, made-up hours or overtime paid.</summary>
/// <param name="AvailableHours">Expected less excused: what there was to log.</param>
/// <param name="OvertimeHours">Time past each day's required hours, as employees see it.</param>
/// <param name="UtilizationPct">Logged ÷ available, a whole percentage (past 100 with overtime); empty with nothing available.</param>
public sealed record UtilizationHours(
    decimal LoggedHours, decimal ExpectedHours, decimal ExcusedHours, decimal AvailableHours, decimal OvertimeHours, int? UtilizationPct)
{
    public static UtilizationHours Of(UtilizationTotalsResponse t) => new(
        ProtonText.Hours(t.LoggedMinutes), ProtonText.Hours(t.ExpectedMinutes), ProtonText.Hours(t.ExcusedMinutes),
        ProtonText.Hours(t.AvailableMinutes), ProtonText.Hours(t.OvertimeMinutes), ProtonAnalysis.Pct(t.Utilization));
}

/// <param name="SharePct">Its part of the person's logged hours, a whole percentage.</param>
public sealed record UtilizationProjectShare(long ProjectId, string Title, decimal Hours, int SharePct);

/// <summary>One Design employee's hours in the month and where they went. Hours only, never money.</summary>
/// <param name="Department">Their department today.</param>
/// <param name="Projects">The projects they logged the most on, at most 5.</param>
/// <param name="MoreProjects">How many more projects they logged on.</param>
public sealed record UtilizationPersonRow(
    long UserId, string Name, string? Department, UtilizationHours Hours, IReadOnlyList<UtilizationProjectShare> Projects, int MoreProjects);

/// <param name="Id">Null for people in no department.</param>
/// <param name="People">How many of its people are counted.</param>
public sealed record UtilizationDepartmentRow(long? Id, string Name, int People, UtilizationHours Hours);

/// <summary>Design employees' utilization in a month, as GET /api/reports/utilization returns it. Hours only.</summary>
/// <param name="Month">Its first day.</param>
/// <param name="InProgress">This month: it runs to today, and so do its expected hours.</param>
public sealed record EmployeeUtilization(
    DateOnly Month,
    bool InProgress,
    UtilizationHours Total,
    IReadOnlyList<UtilizationDepartmentRow> Departments,
    IReadOnlyList<UtilizationPersonRow> People,
    string Note);

/// <summary>
/// How much of their expected hours each Design employee logged in a month, with excused hours, overtime and where the
/// hours went; and each department. It reads everyone's hours ledger, so it is heavy. Hours only: never pay, rates or
/// cost, never a shortfall, made-up hours or paid overtime (Salaries' alone).
/// </summary>
public sealed class AnalyzeEmployeeUtilizationTool(IUtilizationService utilization) : ProtonTool<UtilizationInput>
{
    public const int MaxProjectsPerPerson = 5;

    public override string Name => "analyze_employee_utilization";

    public override string Description =>
        "How much of their expected hours each Design employee logged in a month (this month by default, to today): logged, " +
        "expected, excused and available hours, overtime as employees see it, utilization (logged ÷ (expected − excused)) and " +
        "the projects their hours went to; and the same for each department and the whole studio. Hours only: never pay, a " +
        "shortfall or paid overtime. Admins and site staff log no hours and aren't listed. Design only. Use it for \"how " +
        "utilized is the team\", \"who logged their expected hours\", \"where did each person's hours go this month\".";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Utilization));

    protected override async Task<ProtonToolOutput> RunAsync(UtilizationInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new UtilizationQuery { Month = input.Month };
        await context.ValidateAsync(query, cancellationToken);
        var u = await utilization.MonthAsync(query, cancellationToken);
        var result = new EmployeeUtilization(
            u.Month,
            u.InProgress,
            UtilizationHours.Of(u.Total),
            u.Departments.Select(d => new UtilizationDepartmentRow(d.TeamId, d.Name, d.People, UtilizationHours.Of(d.Hours))).ToList(),
            u.People.Select(p => new UtilizationPersonRow(
                p.UserId, p.Name, p.Department, UtilizationHours.Of(p.Hours),
                p.Projects.Take(MaxProjectsPerPerson).Select(x => new UtilizationProjectShare(
                    x.ProjectId, x.Title, ProtonText.Hours(x.Minutes), ProtonAnalysis.Pct(x.Share) ?? 0)).ToList(),
                Math.Max(0, p.Projects.Count - MaxProjectsPerPerson))).ToList(),
            "Hours only. Utilization is logged ÷ (expected − excused): what someone logged of the hours there were to log; past " +
            "100% with overtime. It is recorded time, not a measure of anyone's work or worth: never call someone lazy, " +
            "unproductive or the like from it, never rank people as better or worse, and never link it to pay. This month " +
            "runs to today, and so do its expected hours. Admins, super admins and site staff log no hours and aren't listed.");

        var period = new DayRange(u.Month, u.InProgress ? context.Today : u.Month.AddMonths(1).AddDays(-1));
        var refs = u.People.Select(p => new ProtonRef(ProtonRefType.Person, p.UserId, p.Name, DomainCodes.Design))
            .Concat(u.People.SelectMany(p => p.Projects.Take(MaxProjectsPerPerson))
                .Select(x => new ProtonRef(ProtonRefType.Project, x.ProjectId, x.Title, DomainCodes.Design)))
            .Distinct()
            .ToList();
        var month = u.Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return new ProtonToolOutput(result, refs, period)
        {
            Basis = ProtonInsights.Basis(
                $"Design utilization, {month}", period, u.InProgress, "Design employees: each person's hours ledger", u.Basis,
                ["utilization is logged ÷ (expected − excused), as a percentage; past 100% with overtime"]),
            Datasets = Datasets(u, period),
        };
    }

    /// <summary>Each person's and each department's hours as tables. Hours only.</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(UtilizationResponse u, DayRange period)
    {
        var words = ProtonDatasets.Period(period);
        var logged = ProtonDatasets.Figure("logged", "Logged hours", ProtonUnit.Hours);
        var expected = ProtonDatasets.Figure("expected", "Expected hours", ProtonUnit.Hours);
        var pct = ProtonDatasets.Figure("utilization", "Utilization", ProtonUnit.Percent);
        var notes = new List<string> { "Hours only. Utilization is logged ÷ (expected − excused); past 100% with overtime." };
        if (u.InProgress)
        {
            notes.Add("This month runs to today, and so do its expected hours.");
        }

        var people = new ProtonDataset(
            "Design: utilization by person",
            [ProtonDatasets.Label("person", "Person"), ProtonDatasets.Label("department", "Department"), logged, expected, pct],
            u.People.Take(ProtonDatasets.MaxRows).Select(p => ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Person, p.UserId, p.Name, DomainCodes.Design),
                ProtonCell.Of(p.Name), ProtonCell.Of(p.Department ?? "No department"), ProtonCell.Of(ProtonText.Hours(p.Hours.LoggedMinutes)),
                ProtonCell.Of(ProtonText.Hours(p.Hours.ExpectedMinutes)), ProtonCell.Of(ProtonAnalysis.Pct(p.Hours.Utilization)))).ToList(),
            words,
            u.People.Count,
            Notes: notes);
        var departments = new ProtonDataset(
            "Design: utilization by department",
            [ProtonDatasets.Label("department", "Department"), ProtonDatasets.Figure("people", "People", ProtonUnit.Count), logged, expected, pct],
            u.Departments.Take(ProtonDatasets.MaxRows).Select(d => ProtonDatasets.Row(
                null, ProtonCell.Of(d.Name), ProtonCell.Of(d.People), ProtonCell.Of(ProtonText.Hours(d.Hours.LoggedMinutes)),
                ProtonCell.Of(ProtonText.Hours(d.Hours.ExpectedMinutes)), ProtonCell.Of(ProtonAnalysis.Pct(d.Hours.Utilization)))).ToList(),
            words,
            u.Departments.Count,
            Notes: notes);
        return ProtonDatasets.WithRows(people, departments);
    }
}
