using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record WorkingTimeArgs(SetWorkingTimeRequest Request);

public sealed record WorkingTimeInput : ProtonActionInput
{
    [Description("Weekdays worked, 0 = Sunday … 6 = Saturday.")]
    public required IReadOnlyList<int> WorkDays { get; init; }

    [Description("Day start, HH:mm.")]
    public required string Start { get; init; }

    [Description("Day end, HH:mm.")]
    public required string End { get; init; }

    public required int BreakMinutes { get; init; }

    [Description("How overtime hours count against normal ones (1.5 for time and a half). Default the current one.")]
    public decimal? OvertimeMultiplier { get; init; }

    [Description("Today or later. Default today.")]
    public DateOnly? EffectiveFrom { get; init; }
}

/// <summary>
/// A new company working week, through POST /api/company/working-time once confirmed. It changes how everyone's hours
/// and overtime count from that day; the card says so in words, with no amounts.
/// </summary>
public sealed class ProposeWorkingTimeTool(IWorkingTimeService workingTime, IEmployeeService employees) : ProtonActionTool<WorkingTimeInput, WorkingTimeArgs>
{
    public override string Name => "propose_working_time";

    public override string Description =>
        "Propose a new company working week (days, hours, break, overtime multiplier) from a day. It changes how " +
        "everyone's hours and overtime count from then. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.SetWorkingTime));

    protected override async Task<(WorkingTimeArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WorkingTimeInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = (await workingTime.GetAsync(cancellationToken)).Current;
        var request = new SetWorkingTimeRequest
        {
            EffectiveFrom = input.EffectiveFrom, WorkDays = input.WorkDays.Distinct().Order().ToList(),
            StartMinute = PayRulesText.Minute(input.Start), EndMinute = PayRulesText.Minute(input.End), BreakMinutes = input.BreakMinutes,
            OvertimeMultiplier = input.OvertimeMultiplier ?? current.OvertimeMultiplier,
        };
        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Work days", PayRulesText.Days(current.WorkDays), PayRulesText.Days(request.WorkDays));
        SettingsText.Compare(changes, "Hours", PayRulesText.Hours(current.StartMinute, current.EndMinute), PayRulesText.Hours(request.StartMinute, request.EndMinute));
        SettingsText.Compare(changes, "Break", $"{current.BreakMinutes} min", $"{request.BreakMinutes} min");
        SettingsText.Compare(changes, "Overtime counts", $"×{current.OvertimeMultiplier:0.##}", $"×{request.OvertimeMultiplier:0.##}");
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        changes.Add(new ProtonChange("From", null, ProjectText.Date(request.EffectiveFrom ?? context.Today)));
        return (new WorkingTimeArgs(request),
            new ProtonPreview("Change the company's working week", changes,
                "From that day, everyone's required hours and overtime count by the new week; earlier days stay as they were. People with a week of their own keep it.",
                Area: "company", Reach: await SettingsText.ReachAsync(employees, null, cancellationToken)),
            null);
    }

    protected override object RequestOf(WorkingTimeArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WorkingTimeArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await workingTime.SetAsync(args.Request, cancellationToken);
        return new ProtonActionDone("Set the new working week.", "working_time", 1, []);
    }
}

public sealed record ScheduleDayInput
{
    [Description("0 = Sunday … 6 = Saturday.")]
    public required int Weekday { get; init; }

    [Description("HH:mm.")]
    public required string Start { get; init; }

    [Description("HH:mm.")]
    public required string End { get; init; }
}

public sealed record ScheduleArgs(long UserId, SetScheduleRequest Request);

public sealed record ScheduleInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("Back to the company's week.")]
    public bool FollowsCompanyWeek { get; init; }

    [Description("Their own working days; a weekday left out is a day off.")]
    public IReadOnlyList<ScheduleDayInput>? Days { get; init; }

    [Description("Tomorrow or later is safest; default today.")]
    public DateOnly? EffectiveFrom { get; init; }
}

/// <summary>Gives someone a working week of their own, or puts them back on the company's, through PUT /api/employees/{id}/schedule once confirmed.</summary>
public sealed class ProposeScheduleTool(IEmployeeService employees, IScheduleService schedules) : ProtonActionTool<ScheduleInput, ScheduleArgs>
{
    public override string Name => "propose_schedule";

    public override string Description =>
        "Propose a working week of someone's own (days and hours), or putting them back on the company's week, from a " +
        "day. It changes how their hours and overtime count. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<SchedulesController>(nameof(SchedulesController.Set));

    protected override async Task<(ScheduleArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ScheduleInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        if (!input.FollowsCompanyWeek && input.Days is not { Count: > 0 })
        {
            throw new ProtonToolInputException("Give their working days, or follows_company_week.");
        }

        var request = new SetScheduleRequest
        {
            EffectiveFrom = input.EffectiveFrom, FollowsDefault = input.FollowsCompanyWeek,
            Days = input.FollowsCompanyWeek ? [] : input.Days!.Select(d => new ScheduleDayRequest(d.Weekday, PayRulesText.Minute(d.Start), PayRulesText.Minute(d.End))).ToList(),
        };
        var after = input.FollowsCompanyWeek
            ? "the company's week"
            : string.Join(", ", request.Days.OrderBy(d => d.Weekday).Select(d => $"{PayRulesText.Day(d.Weekday)} {PayRulesText.Hours(d.StartMinute, d.EndMinute)}"));
        return (new ScheduleArgs(person.Id, request),
            new ProtonPreview($"Change {AccessText.Name(person)}'s working week",
                [new ProtonChange("Week", null, after), new ProtonChange("From", null, ProjectText.Date(request.EffectiveFrom ?? context.Today))],
                "Their required hours and overtime count by it from that day."),
            AccessText.Ref(person));
    }

    protected override object RequestOf(ScheduleArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ScheduleArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var schedule = await schedules.SetAsync(args.UserId, args.Request, cancellationToken);
        return new ProtonActionDone($"Set {schedule.Name}'s working week.", "schedule", args.UserId, []);
    }
}

public sealed record HourExcuseArgs(long UserId, AddHourExcuseRequest Request);

public sealed record HourExcuseInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("The month, YYYY-MM.")]
    public required string Month { get; init; }

    [Description("A number of minutes excused that month; or give days.")]
    public int? Minutes { get; init; }

    [Description("Days of that month excused, each worth the day's required hours.")]
    public IReadOnlyList<DateOnly>? Days { get; init; }
}

/// <summary>
/// Excuses some of someone's required hours in a month (sick days, an errand), through POST
/// /api/employees/{id}/hour-excuses once confirmed. The card says what is excused, never what it is worth.
/// </summary>
public sealed class ProposeHourExcuseTool(IEmployeeService employees, IHourExcuseService excuses) : ProtonActionTool<HourExcuseInput, HourExcuseArgs>
{
    public override string Name => "propose_hour_excuse";

    public override string Description =>
        "Propose excusing some of someone's required hours in a month: a number of minutes, or whole days. Nothing " +
        "changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<HourExcusesController>(nameof(HourExcusesController.Add));

    protected override async Task<(HourExcuseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        HourExcuseInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Minutes is null == input.Days is not { Count: > 0 })
        {
            throw new ProtonToolInputException("Give minutes or days, one of the two.");
        }

        var person = await employees.GetAsync(input.UserId, cancellationToken);
        var request = input.Minutes is { } minutes
            ? new AddHourExcuseRequest { Month = input.Month.Trim(), Kind = ExcuseKind.Minutes, Minutes = minutes }
            : new AddHourExcuseRequest { Month = input.Month.Trim(), Kind = ExcuseKind.Days, Days = input.Days!.Distinct().Order().ToList() };
        var what = request.Kind == ExcuseKind.Minutes
            ? WorkText.Hours(request.Minutes!.Value)
            : string.Join(", ", request.Days.Select(d => ProjectText.Date(d)));
        return (new HourExcuseArgs(person.Id, request),
            new ProtonPreview($"Excuse some of {AccessText.Name(person)}'s hours",
                [new ProtonChange("Month", null, request.Month), new ProtonChange("Excused", null, what)],
                "They aren't counted as missing hours that month."),
            AccessText.Ref(person));
    }

    protected override object RequestOf(HourExcuseArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(HourExcuseArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var excuse = await excuses.AddAsync(args.UserId, args.Request, cancellationToken);
        return new ProtonActionDone("Excused the hours.", "hour_excuse", excuse.Id, []);
    }
}

public enum ProtonDeleteKind
{
    Requirement,
    Meeting,
    Client,
}

public sealed record DeleteRecordArgs(ProtonDeleteKind Kind, long Id, long? ProjectId);

public sealed record DeleteRecordInput : ProtonActionInput
{
    [Description("requirement, meeting or client.")]
    public required ProtonDeleteKind Kind { get; init; }

    [Description("The requirement, meeting or client.")]
    public required long Id { get; init; }

    [Description("The project, for a requirement or a meeting.")]
    public long? ProjectId { get; init; }
}

/// <summary>
/// Deletes a requirement, a meeting, or a client no project uses, through its DELETE endpoint once confirmed. It can't
/// be undone, so the user types the project's (or the client's) name.
/// </summary>
public sealed class ProposeDeleteRecordTool(
    IProjectService projects, IRequirementService requirements, IMeetingService meetings, IClientService clients)
    : ProtonActionTool<DeleteRecordInput, DeleteRecordArgs>
{
    public override string Name => "propose_delete";

    public override string Description =>
        "Propose deleting a requirement, a meeting, or a client no project uses (archive a client instead when it has " +
        "history). It can't be undone; the user types the project's or client's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // each kind's own endpoint is checked as well
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<RequirementsController>(nameof(RequirementsController.Delete));

    private static readonly IReadOnlyDictionary<ProtonDeleteKind, ProtonEndpoint> Kinds = new Dictionary<ProtonDeleteKind, ProtonEndpoint>
    {
        [ProtonDeleteKind.Requirement] = ProtonEndpoint.OfWrite<RequirementsController>(nameof(RequirementsController.Delete)),
        [ProtonDeleteKind.Meeting] = ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.Delete)),
        [ProtonDeleteKind.Client] = ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Delete)),
    };

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Kinds.Values.ToList();

    protected override ProtonEndpoint StepOf(DeleteRecordArgs args) => Kinds[args.Kind];

    protected override async Task<(DeleteRecordArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DeleteRecordInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.RequireAsync(Kinds[input.Kind]);
        switch (input.Kind)
        {
            case ProtonDeleteKind.Client:
                var client = await clients.GetAsync(input.Id, cancellationToken);
                if (client.ProjectCount > 0)
                {
                    throw new ProtonToolInputException($"{client.DisplayName} has {client.ProjectCount} project(s): archive it instead.");
                }

                return (new DeleteRecordArgs(input.Kind, client.Id, null),
                    new ProtonPreview($"Delete the client {client.DisplayName}", [new ProtonChange("Client", client.DisplayName, "deleted")],
                        "Its contacts go with it.", Confirm: ProtonConfirm.Typed, Phrase: client.DisplayName, Irreversible: true),
                    ClientText.Ref(client));
            case ProtonDeleteKind.Requirement:
            {
                var project = await projects.GetAsync(input.ProjectId ?? throw new ProtonToolInputException("Give the project."), cancellationToken);
                var requirement = await RequirementText.FindAsync(requirements, project.Id, input.Id, cancellationToken);
                return (new DeleteRecordArgs(input.Kind, requirement.Id, project.Id),
                    new ProtonPreview($"Delete a requirement of {project.Title}", [new ProtonChange("Requirement", ProtonText.Cut(requirement.Text, 200), "deleted")],
                        Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true),
                    ProjectText.Ref(project));
            }
            default:
            {
                var project = await projects.GetAsync(input.ProjectId ?? throw new ProtonToolInputException("Give the project."), cancellationToken);
                var meeting = await MeetingText.FindAsync(meetings, project.Id, input.Id, cancellationToken);
                return (new DeleteRecordArgs(input.Kind, meeting.Id, project.Id),
                    new ProtonPreview($"Delete a meeting of {project.Title}",
                        [new ProtonChange("Meeting", $"{meeting.Topic ?? "Meeting"}, {ProjectText.Date(DateOnly.FromDateTime(meeting.StartsAt))}", "deleted")],
                        Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true),
                    ProjectText.Ref(project));
            }
        }
    }

    protected override object? RequestOf(DeleteRecordArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(DeleteRecordArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.RequireAsync(Kinds[args.Kind]);
        switch (args.Kind)
        {
            case ProtonDeleteKind.Client:
                await clients.DeleteAsync(args.Id, cancellationToken);
                return new ProtonActionDone("Deleted the client.", "client", args.Id, []);
            case ProtonDeleteKind.Requirement:
                await requirements.DeleteAsync(args.ProjectId!.Value, args.Id, cancellationToken);
                return new ProtonActionDone("Deleted the requirement.", "requirement", args.Id, []);
            default:
                await meetings.DeleteAsync(args.Id, cancellationToken);
                return new ProtonActionDone("Deleted the meeting.", "meeting", args.Id, []);
        }
    }
}

internal static class PayRulesText
{
    private static readonly string[] Names = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static int Minute(string time)
        => TimeOnly.TryParseExact(time.Trim(), "H:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
            ? t.Hour * 60 + t.Minute
            : throw new ProtonToolInputException("Times are HH:mm, for example 09:00.");

    public static string Day(int weekday) => weekday is >= 0 and <= 6 ? Names[weekday] : throw new ProtonToolInputException("Weekdays are 0 (Sunday) to 6 (Saturday).");

    public static string Days(IEnumerable<int> days) => string.Join(", ", days.Order().Select(Day));

    public static string Hours(int start, int end) => $"{start / 60:00}:{start % 60:00}–{end / 60:00}:{end % 60:00}";
}
