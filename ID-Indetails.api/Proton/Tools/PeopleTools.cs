using System.ComponentModel;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record PersonIdInput
{
    public required long UserId { get; init; }
}

public sealed record PersonCard(
    long Id,
    string Name,
    string Email,
    string? Phone,
    string Role,
    string? Domain,
    string? Team,
    string? Position,
    UserStatus Status,
    DateOnly? JoinedOn,
    DateOnly? LeftOn,
    DateTime? LastSeenAt,
    bool Online);

/// <summary>A person's profile, as GET /api/employees/{id} returns it, without their pay.</summary>
public sealed class GetPersonTool(IEmployeeService employees) : ProtonTool<PersonIdInput>
{
    public override string Name => "get_person";

    public override string Description => "A person's profile: role, domain, team, position, status, contact details, when they joined.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<EmployeesController>(nameof(EmployeesController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(PersonIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var e = await employees.GetAsync(input.UserId, cancellationToken);
        var name = $"{e.FirstName} {e.LastName}".Trim();
        return ProtonToolOutput.Of(
            new PersonCard(e.Id, name, e.Email, e.Phone, e.Role, e.Domain, e.Team?.Name, e.Position, e.Status,
                e.JoinedOn, e.LeftOn, e.LastSeenAt, e.Online),
            new ProtonRef(ProtonRefType.Person, e.Id, name, e.Domain));
    }
}

public sealed record PersonWorkInput
{
    public required long UserId { get; init; }

    [Description("The period for the per-project breakdown. Default this_month.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

public sealed record PersonProjectHours(long ProjectId, string Title, ProjectStatus Status, decimal Hours, decimal OvertimeHours, int Entries, DateTime LastWorkAt);

public sealed record PersonDay(DateOnly Day, decimal Hours);

public sealed record PersonWork(
    long UserId,
    string Name,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PersonProjectHours> ProjectsInPeriod,
    decimal TodayHours,
    IReadOnlyList<PersonDay> ThisWeekByDay,
    decimal ThisWeekHours,
    decimal LastWeekHours,
    decimal ThisMonthHours,
    decimal ThisQuarterHours,
    decimal AllTimeHours,
    DateOnly? FirstEntryDay,
    string Note);

/// <summary>One person's logged hours, as GET /api/employees/{id}/work-summary returns them. Design only.</summary>
public sealed class GetPersonWorkTool(IMyWorkService myWork) : ProtonTool<PersonWorkInput>
{
    public override string Name => "get_person_work";

    public override string Description =>
        "One person's hours: which projects they worked on in a period and how long, plus this week by day, last " +
        "week, this month and quarter. Design only.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<EmployeeWorkController>(nameof(EmployeeWorkController.Summary));

    protected override async Task<ProtonToolOutput> RunAsync(PersonWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisMonth);
        var query = new WorkSummaryQuery { From = range.From, To = range.To };
        await context.ValidateAsync(query, cancellationToken);
        var s = await myWork.SummaryForPersonAsync(input.UserId, query, cancellationToken);

        var projects = s.ByProject
            .Select(p => new PersonProjectHours(p.ProjectId, p.Title, p.Status, ProtonText.Hours(p.Minutes), ProtonText.Hours(p.OvertimeMinutes), p.Entries, p.LastStartedAt))
            .ToList();
        var work = new PersonWork(
            s.Person.Id, s.Person.Name, range.From, range.To, projects,
            ProtonText.Hours(s.TodayMinutes),
            s.Week.Select(d => new PersonDay(d.Day, ProtonText.Hours(d.Minutes))).ToList(),
            ProtonText.Hours(s.ThisWeekMinutes), ProtonText.Hours(s.LastWeekMinutes), ProtonText.Hours(s.ThisMonthMinutes),
            ProtonText.Hours(s.ThisQuarterMinutes), ProtonText.Hours(s.TotalMinutes), s.FirstEntryDay,
            "Admins and super admins don't log hours, so they have none.");
        return new ProtonToolOutput(
            work,
            [new ProtonRef(ProtonRefType.Person, s.Person.Id, s.Person.Name), .. projects.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, DomainCodes.Design))],
            range);
    }
}

public enum PeopleSort
{
    MostThisWeek,
    MostLast30Days,
    LongestWithoutLogging,
    Name,
}

public sealed record PeopleActivityInput
{
    public long? TeamId { get; init; }

    [Description("Name, email or position words.")]
    public string? Search { get; init; }

    [Description("Only people who logged nothing in this many days (or never).")]
    public int? InactiveForDays { get; init; }

    public PeopleSort? Sort { get; init; }

    [Description("Rows to return, 1 to 40 (default 25).")]
    public int? Limit { get; init; }
}

public sealed record PersonActivityRow(
    long Id,
    string Name,
    string? Position,
    string? Team,
    string Role,
    UserStatus Status,
    decimal ThisWeekHours,
    decimal Last30DaysHours,
    decimal ThisQuarterHours,
    decimal OvertimeHoursThisMonth,
    DateTime? LastLoggedAt,
    ProtonNamed? FocusProject);

public sealed record PeopleActivity(int Total, int Shown, IReadOnlyList<PersonActivityRow> People);

/// <summary>Everyone's recent logging, as GET /api/work/people returns it, without rates or labour cost. Design only.</summary>
public sealed class ListPeopleActivityTool(IWorkInsightsService insights, IClock clock) : ProtonTool<PeopleActivityInput>
{
    public override string Name => "list_people_activity";

    public override string Description =>
        "Design people and their recent logging: hours this week, last 30 days and this quarter, when they last " +
        "logged, and their main project of the last two weeks. Use it for 'who hasn't logged recently' and workloads.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.People));

    protected override async Task<ProtonToolOutput> RunAsync(PeopleActivityInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.InactiveForDays is < 1 or > 365)
        {
            throw new ProtonToolInputException("inactive_for_days must be between 1 and 365.");
        }

        var query = new PeopleWorkQuery { TeamId = input.TeamId, Search = input.Search };
        await context.ValidateAsync(query, cancellationToken);
        var people = await insights.PeopleAsync(query, cancellationToken);

        var cutoff = context.Today.AddDays(-(input.InactiveForDays ?? 0));
        var matching = people
            .Where(p => p.Status != UserStatus.Deactivated)
            .Where(p => input.InactiveForDays is null || p.LastStartedAt is null || clock.ToCompanyDate(p.LastStartedAt.Value) < cutoff)
            .ToList();
        IEnumerable<DTO.Response.WorkEntries.PersonWorkResponse> sorted = (input.Sort ?? PeopleSort.MostThisWeek) switch
        {
            PeopleSort.MostLast30Days => matching.OrderByDescending(p => p.MinutesLast30Days),
            PeopleSort.LongestWithoutLogging => matching.OrderBy(p => p.LastStartedAt ?? DateTime.MinValue),
            PeopleSort.Name => matching.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => matching.OrderByDescending(p => p.WeekMinutes),
        };

        var rows = sorted.Take(ProtonText.Limit(input.Limit, 25, 40))
            .Select(p => new PersonActivityRow(
                p.Id, p.Name, p.Position, p.TeamName, p.Role, p.Status,
                ProtonText.Hours(p.WeekMinutes), ProtonText.Hours(p.MinutesLast30Days), ProtonText.Hours(p.QuarterMinutes),
                ProtonText.Hours(p.OvertimeMinutesThisMonth), p.LastStartedAt,
                p.Focus is { } f ? new ProtonNamed(f.ProjectId, f.Title) : null))
            .ToList();
        return new ProtonToolOutput(
            new PeopleActivity(matching.Count, rows.Count, rows),
            rows.Select(r => new ProtonRef(ProtonRefType.Person, r.Id, r.Name, DomainCodes.Design)).ToList());
    }
}
