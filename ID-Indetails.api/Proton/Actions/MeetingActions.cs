using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The meeting as it was when proposed, for an update: if it changed since, the update is refused.</param>
public sealed record MeetingArgs(long ProjectId, long? MeetingId, SaveMeetingRequest Request, SaveMeetingRequest? Before);

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
        var meeting = await meetings.CreateAsync(args.ProjectId, args.Request, cancellationToken);
        return await MeetingText.DoneAsync(projects, args.ProjectId, $"Set the meeting for {MeetingText.When(clock, args.Request)}.", meeting.Id, cancellationToken);
    }
}

public sealed record UpdateMeetingInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

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
        "Propose moving a meeting to another day or time, changing its length, or renaming it (use list_meetings for " +
        "its id). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    protected override string? OpensLabel(MeetingArgs args, ProtonRef? target) => MeetingText.OpensLabel(target);

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.Update));

    protected override async Task<(MeetingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateMeetingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var current = await MeetingText.FindAsync(meetings, project.Id, input.MeetingId, cancellationToken);
        MeetingRules.CheckChange(
            context.Access.Permissions.Contains(PermissionCodes.MeetingManage), context.Access.Permissions.Contains(PermissionCodes.MeetingCreate),
            current.CreatedBy?.Id, context.Access.UserId).ThrowIfDenied();
        MeetingRules.CheckProject(project.Status).ThrowIfDenied();

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

        return (new MeetingArgs(project.Id, current.Id, after, before),
            new ProtonPreview($"Change the meeting {current.Topic ?? MeetingText.When(clock, before)}", changes), ProjectText.Ref(project));
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

    public static async Task<MeetingResponse> FindAsync(IMeetingService meetings, long projectId, long meetingId, CancellationToken cancellationToken)
        => (await meetings.ListForProjectAsync(projectId, cancellationToken)).FirstOrDefault(m => m.Id == meetingId)
            ?? throw new NotFoundException("Meeting not found.");

    public static string? OpensLabel(ProtonRef? project) => project is null ? null : $"{project.Name}: meetings";

    /// <summary>A meeting change done: it lives on its project's meetings tab.</summary>
    public static async Task<ProtonActionDone> DoneAsync(IProjectService projects, long projectId, string message, long meetingId, CancellationToken cancellationToken)
    {
        var project = ProjectText.Ref(await projects.GetAsync(projectId, cancellationToken));
        return new ProtonActionDone(message, "meeting", meetingId, [project], ProtonUiCommand.Open(project, ProtonProjectSection.Meetings));
    }
}
