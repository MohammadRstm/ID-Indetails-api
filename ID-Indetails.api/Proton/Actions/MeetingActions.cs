using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The meeting as it was when proposed, for an update: if it changed since, the update is refused.</param>
/// <param name="ProjectId">Null for a general meeting, on no project.</param>
public sealed record MeetingArgs(long? ProjectId, long? MeetingId, SaveMeetingRequest Request, SaveMeetingRequest? Before);

public sealed record ScheduleMeetingInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("The day, in the company's time zone.")]
    public required DateOnly Day { get; init; }

    [Description("Start time, HH:mm (24-hour), in the company's time zone.")]
    public required string Start { get; init; }

    [Description("Length in minutes. Default 60.")]
    public int? Minutes { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }
}

/// <summary>Sets a meeting on a project, through POST /api/projects/{id}/meetings once confirmed.</summary>
public sealed class ProposeScheduleMeetingTool(IProjectService projects, IMeetingService meetings, IClock clock)
    : ProtonActionTool<ScheduleMeetingInput, MeetingArgs>
{
    public override string Name => "propose_schedule_meeting";

    public override string Description =>
        "Propose setting a meeting on a project's calendar (a time slot in the app, no invitation is emailed). " +
        "Times are the company's. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(MeetingArgs args, ProtonRef? target) => MeetingText.OpensLabel(target);

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.Create));

    protected override async Task<(MeetingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ScheduleMeetingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        MeetingRules.CheckProject(project.Status).ThrowIfDenied();
        var request = MeetingText.RequestOf(clock, input.Day, input.Start, input.Minutes ?? 60, MeetingText.Clean(input.Topic), MeetingText.Clean(input.Description));
        MeetingText.CheckTimes(clock, request);

        var changes = new List<ProtonChange>
        {
            new("When", null, MeetingText.When(clock, request)),
            new("Topic", null, request.Topic ?? "none"),
        };
        if (request.Description is not null)
        {
            changes.Add(new ProtonChange("Description", null, ProtonText.Cut(request.Description, 200)!));
        }

        return (new MeetingArgs(project.Id, null, request, null), new ProtonPreview($"Set a meeting for {project.Title}", changes), ProjectText.Ref(project));
    }

    protected override object RequestOf(MeetingArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(MeetingArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var meeting = await meetings.CreateAsync(args.ProjectId!.Value, args.Request, cancellationToken);
        return await MeetingText.DoneAsync(projects, args.ProjectId, $"Set the meeting for {MeetingText.When(clock, args.Request)}.", meeting.Id, cancellationToken);
    }
}

public sealed record UpdateMeetingInput : ProtonActionInput
{
    [Description("The project the meeting is on. Leave out for a general meeting (on no project).")]
    public long? ProjectId { get; init; }

    [Description("From list_meetings.")]
    public required long MeetingId { get; init; }

    [Description("A new day, in the company's time zone.")]
    public DateOnly? Day { get; init; }

    [Description("A new start time, HH:mm.")]
    public string? Start { get; init; }

    [Description("A new length in minutes.")]
    public int? Minutes { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }
}

/// <summary>Moves, resizes or renames a meeting, through PUT /api/meetings/{id} once confirmed.</summary>
public sealed class ProposeUpdateMeetingTool(IProjectService projects, IMeetingService meetings, IClock clock)
    : ProtonActionTool<UpdateMeetingInput, MeetingArgs>
{
    public override string Name => "propose_update_meeting";

    public override string Description =>
        "Propose moving a meeting to another day or time, changing its length, or renaming it, on a project or a general " +
        "meeting (use list_meetings for its id). A general meeting keeps its people. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(MeetingArgs args, ProtonRef? target) => MeetingText.OpensLabel(target);

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.Update));

    protected override async Task<(MeetingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateMeetingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = input.ProjectId is { } projectId ? await projects.GetAsync(projectId, cancellationToken) : null;
        var current = await MeetingText.FindAsync(meetings, project?.Id, input.MeetingId, cancellationToken);
        MeetingRules.CheckChange(
            context.Access.Permissions.Contains(PermissionCodes.MeetingManage), context.Access.Permissions.Contains(PermissionCodes.MeetingCreate),
            current.CreatedBy?.Id, context.Access.UserId).ThrowIfDenied();
        if (project is not null)
        {
            MeetingRules.CheckProject(project.Status).ThrowIfDenied();
        }

        var before = MeetingText.RequestOf(current);
        var start = clock.ToCompanyTime(current.StartsAt);
        var after = MeetingText.RequestOf(
            clock,
            input.Day ?? DateOnly.FromDateTime(start),
            input.Start ?? start.ToString("HH:mm", CultureInfo.InvariantCulture),
            input.Minutes ?? current.Minutes,
            input.Topic is null ? before.Topic : MeetingText.Clean(input.Topic),
            input.Description is null ? before.Description : MeetingText.Clean(input.Description));
        MeetingText.CheckTimes(clock, after);

        var changes = new List<ProtonChange>();
        if (after.StartsAt != before.StartsAt || after.EndsAt != before.EndsAt)
        {
            changes.Add(new ProtonChange("When", MeetingText.When(clock, before), MeetingText.When(clock, after)));
        }

        if (after.Topic != before.Topic)
        {
            changes.Add(new ProtonChange("Topic", before.Topic ?? "none", after.Topic ?? "none"));
        }

        if (after.Description != before.Description)
        {
            changes.Add(new ProtonChange("Description", ProtonText.Cut(before.Description, 200) ?? "none", ProtonText.Cut(after.Description, 200) ?? "none"));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new MeetingArgs(project?.Id, current.Id, after, before),
            new ProtonPreview($"Change the meeting {current.Topic ?? MeetingText.When(clock, before)}", changes),
            project is null ? null : ProjectText.Ref(project));
    }

    protected override object RequestOf(MeetingArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(MeetingArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await MeetingText.FindAsync(meetings, args.ProjectId, args.MeetingId!.Value, cancellationToken);
        if (MeetingText.RequestOf(current) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The meeting changed since Proton proposed this. Ask again.");
        }

        var meeting = await meetings.UpdateAsync(current.Id, args.Request, cancellationToken);
        return await MeetingText.DoneAsync(projects, args.ProjectId, $"Updated the meeting: {MeetingText.When(clock, args.Request)}.", meeting.Id, cancellationToken);
    }
}

/// <param name="Request">What the endpoint takes; its attendees are everyone chosen but the user.</param>
public sealed record GeneralMeetingArgs(SaveGeneralMeetingRequest Request);

public sealed record CreateGeneralMeetingInput : ProtonActionInput
{
    [Description("The day, in the company's time zone.")]
    public required DateOnly Day { get; init; }

    [Description("Start time, HH:mm (24-hour), in the company's time zone.")]
    public required string Start { get; init; }

    [Description("Length in minutes. Default 60.")]
    public int? Minutes { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }

    [Description("Who it is with, besides the user: at least one active person (ids from find).")]
    public required IReadOnlyList<long> AttendeeIds { get; init; }

    [Description("design or execution: whose meeting managers also see it. Leave out for the usual.")]
    public ProtonDomain? Domain { get; init; }
}

/// <summary>
/// Sets a meeting on no project, with the people chosen, through POST /api/meetings once confirmed. They are told in
/// the app (a notification); no invitation is emailed.
/// </summary>
public sealed class ProposeCreateGeneralMeetingTool(IEmployeeService employees, IMeetingService meetings, IClock clock)
    : ProtonActionTool<CreateGeneralMeetingInput, GeneralMeetingArgs>
{
    public override string Name => "propose_create_general_meeting";

    public override string Description =>
        "Propose setting a general meeting (on no project) with the people the user named (find for their ids): a day, a " +
        "start time and a length in the company's time zone. The people are told in the app; no invitation is emailed. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.CreateGeneral));

    protected override async Task<(GeneralMeetingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateGeneralMeetingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var times = MeetingText.RequestOf(clock, input.Day, input.Start, input.Minutes ?? 60, MeetingText.Clean(input.Topic), MeetingText.Clean(input.Description));
        MeetingText.CheckTimes(clock, times);

        var domain = input.Domain.Code();
        var meetingDomain = MeetingText.GeneralDomain(context.Access, domain);
        var invited = input.AttendeeIds.Distinct().Where(id => id != context.Access.UserId).ToList();
        if (invited.Count == 0)
        {
            throw new ProtonToolInputException("Choose who the meeting is with, besides the user (ids from find).");
        }

        var names = new List<string>();
        foreach (var id in invited)
        {
            // someone out of the user's reach reads as not found
            var person = await employees.GetAsync(id, cancellationToken);
            var name = MeetingText.NameOf(person);
            if (person.Status != UserStatus.Active)
            {
                throw new ProtonToolInputException($"{name} isn't active, so can't be invited.");
            }

            if (person.Domain is not null && meetingDomain is not null && person.Domain != meetingDomain)
            {
                throw new ProtonToolInputException($"{name} isn't in {meetingDomain}: choose people of the meeting's business.");
            }

            names.Add(name);
        }

        var changes = new List<ProtonChange>
        {
            new("When", null, MeetingText.When(clock, times)),
            new("With", null, string.Join(", ", names)),
            new("Topic", null, times.Topic ?? "none"),
        };
        if (times.Description is not null)
        {
            changes.Add(new ProtonChange("Description", null, ProtonText.Cut(times.Description, 200)!));
        }

        var request = new SaveGeneralMeetingRequest
        {
            StartsAt = times.StartsAt, EndsAt = times.EndsAt, Topic = times.Topic, Description = times.Description, Domain = domain, AttendeeIds = invited,
        };
        var told = names.Count == 1 ? names[0] + " is" : $"The {names.Count} people are";
        return (new GeneralMeetingArgs(request),
            new ProtonPreview("Set a general meeting", changes, $"{told} told in the app; no invitation is emailed.", Area: meetingDomain),
            null);
    }

    protected override object RequestOf(GeneralMeetingArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(GeneralMeetingArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var meeting = await meetings.CreateGeneralAsync(args.Request, cancellationToken);
        var when = MeetingText.When(clock, new SaveMeetingRequest { StartsAt = args.Request.StartsAt, EndsAt = args.Request.EndsAt });
        // a general meeting has no page to open
        return new ProtonActionDone($"Set the meeting for {when}.", "meeting", meeting.Id, []);
    }
}

/// <summary>Shared wording and lookups of the meeting actions.</summary>
internal static class MeetingText
{
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A day and time in the company's time zone, as the meeting endpoint takes it (UTC).</summary>
    public static SaveMeetingRequest RequestOf(IClock clock, DateOnly day, string start, int minutes, string? topic, string? description)
    {
        if (!TimeOnly.TryParseExact(start.Trim(), "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new ProtonToolInputException("The start time must be HH:mm, for example 14:30.");
        }

        if (minutes is < MeetingRules.MinMinutes or > 24 * 60)
        {
            throw new ProtonToolInputException($"A meeting lasts {MeetingRules.MinMinutes} minutes to a day.");
        }

        var startsAt = clock.FromCompanyTime(day, time.Hour * 60 + time.Minute);
        return new SaveMeetingRequest { StartsAt = startsAt, EndsAt = startsAt.AddMinutes(minutes), Topic = topic, Description = description };
    }

    public static SaveMeetingRequest RequestOf(MeetingResponse m)
        => new() { StartsAt = m.StartsAt, EndsAt = m.EndsAt, Topic = m.Topic, Description = m.Description };

    public static void CheckTimes(IClock clock, SaveMeetingRequest request)
        => MeetingRules.CheckTimes(request.StartsAt, request.EndsAt,
            DateOnly.FromDateTime(clock.ToCompanyTime(request.StartsAt)), DateOnly.FromDateTime(clock.ToCompanyTime(request.EndsAt.AddMinutes(-1)))).ThrowIfDenied();

    public static string When(IClock clock, SaveMeetingRequest request)
    {
        var start = clock.ToCompanyTime(request.StartsAt);
        var end = clock.ToCompanyTime(request.EndsAt);
        return $"{start.ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}–{end.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>A meeting by its id: on its project, or, with no project, a general meeting the user may see.</summary>
    public static async Task<MeetingResponse> FindAsync(IMeetingService meetings, long? projectId, long meetingId, CancellationToken cancellationToken)
        => (projectId is { } project
                ? await meetings.ListForProjectAsync(project, cancellationToken)
                : (await meetings.ListAsync(new MeetingListQuery(), cancellationToken)).Where(m => m.Project is null).ToList())
            .FirstOrDefault(m => m.Id == meetingId)
            ?? throw new NotFoundException("Meeting not found.");

    public static string NameOf(EmployeeResponse person) => $"{person.FirstName} {person.LastName}".Trim();

    /// <summary>
    /// The business a general meeting belongs to, as the meeting service decides: the one asked for (which the user must
    /// see), otherwise the whole company (null) for someone who sees it, or their own business.
    /// </summary>
    public static string? GeneralDomain(UserAccess access, string? asked)
    {
        if (asked is not null)
        {
            return access.CanSee(asked) ? asked : throw new NotFoundException("Domain not found.");
        }

        return access.Has(PermissionCodes.CompanyView)
            ? null
            : access.VisibleBusinesses.FirstOrDefault() ?? throw new ProtonToolInputException("Say which business the meeting is in: design or execution.");
    }

    public static string? OpensLabel(ProtonRef? project) => project is null ? null : $"{project.Name}: meetings";

    /// <summary>A meeting change done: a project's lives on its meetings tab; a general meeting has no page to open.</summary>
    public static async Task<ProtonActionDone> DoneAsync(IProjectService projects, long? projectId, string message, long meetingId, CancellationToken cancellationToken)
    {
        if (projectId is null)
        {
            return new ProtonActionDone(message, "meeting", meetingId, []);
        }

        var project = ProjectText.Ref(await projects.GetAsync(projectId.Value, cancellationToken));
        return new ProtonActionDone(message, "meeting", meetingId, [project], ProtonUiCommand.Open(project, ProtonProjectSection.Meetings));
    }
}
