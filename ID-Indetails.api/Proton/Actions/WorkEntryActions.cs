using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Teams.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

public sealed record CommentWorkEntryArgs(long WorkEntryId, AddWorkEntryCommentRequest Request);

public sealed record CommentWorkEntryInput : ProtonActionInput
{
    [Description("From list_work_entries or get_work_entry.")]
    public required long WorkEntryId { get; init; }

    [Description("The comment, in the user's words.")]
    public required string Body { get; init; }

    [Description("Also post it to the author's team chat, mentioning everyone there. Only when the user asked for that.")]
    public bool SendToGroup { get; init; }
}

/// <summary>
/// Comments on a work entry, through POST /api/work-entries/{id}/comments once confirmed. Sharing it to the author's
/// team chat too is a message to all of them: it needs Messaging switched on, a question the user typed, and the
/// team's name typed to confirm.
/// </summary>
public sealed class ProposeCommentWorkEntryTool(
    IWorkEntryQueryService workEntries,
    IWorkEntryCommentService comments,
    IEmployeeService employees,
    ITeamService teams,
    IClock clock,
    IOptions<ProtonOptions> options)
    : ProtonActionTool<CommentWorkEntryInput, CommentWorkEntryArgs>
{
    public override string Name => "propose_comment_work_entry";

    public override string Description =>
        "Propose commenting on someone's logged work entry; its author is notified. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.AddComment));

    protected override async Task<(CommentWorkEntryArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CommentWorkEntryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = (await workEntries.GetAsync(input.WorkEntryId, cancellationToken)).Entry;
        var request = new AddWorkEntryCommentRequest { Body = input.Body.Trim(), SendToGroup = input.SendToGroup };
        var preview = new ProtonPreview(
            $"Comment on {entry.Person.Name}'s work",
            [
                new ProtonChange("Entry", null, $"{clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}"),
                new ProtonChange("Comment", null, request.Body),
            ],
            entry.Person.Id == context.Access.UserId ? null : $"{entry.Person.Name} is notified.");
        if (input.SendToGroup)
        {
            CheckGroupAllowed(context.Turn);
            var team = (await employees.GetAsync(entry.Person.Id, cancellationToken)).Team
                ?? throw new ProtonToolInputException($"{entry.Person.Name} isn't in a team, so there is no team chat to share it to.");
            var members = (await TeamText.FindAsync(teams, team.Id, cancellationToken)).MemberCount;
            preview = preview with
            {
                Changes = [.. preview.Changes, new ProtonChange("Also posted to", null, $"{team.Name} team chat, mentioning everyone")],
                Confirm = ProtonConfirm.Typed,
                Phrase = team.Name,
                Irreversible = true,
                Reach = $"Reaches {members} {(members == 1 ? "person" : "people")}.",
            };
        }

        return (new CommentWorkEntryArgs(entry.Id, request), preview, new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object RequestOf(CommentWorkEntryArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CommentWorkEntryArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Request.SendToGroup && !options.Value.Actions.IsOn(ProtonFamily.Messaging))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionsOff, "Sending messages is switched off for Proton now. You can share the comment in the app.");
        }

        var comment = await comments.AddAsync(args.WorkEntryId, args.Request, cancellationToken);
        return new ProtonActionDone(comment.SentToGroup ? "Posted the comment and shared it to the team chat." : "Posted the comment.", "work_entry_comment", comment.Id, []);
    }

    /// <summary>The comment family is Core; sharing it is a message, held to the Messaging family's rules.</summary>
    private void CheckGroupAllowed(ProtonTurnOrigin turn)
    {
        if (!options.Value.Actions.IsOn(ProtonFamily.Messaging))
        {
            throw new ProtonToolInputException("Sharing to a team chat isn't switched on for Proton yet: post the comment alone, or share it in the app.");
        }

        if (turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("Sharing to a team chat needs the user to ask for it in their own words.");
        }
    }
}

/// <param name="Before">The entry as it was when proposed, for an edit: if it changed since, the edit is refused.</param>
public sealed record MyWorkArgs(long? EntryId, SaveWorkEntryRequest Request, SaveWorkEntryRequest? Before);

public sealed record LogWorkInput : ProtonActionInput
{
    [Description("The project worked on (from find or list_my_work).")]
    public required long ProjectId { get; init; }

    [Description("The day, in the company's time zone. Default today.")]
    public DateOnly? Day { get; init; }

    [Description("Start, HH:mm (24-hour), company time.")]
    public required string Start { get; init; }

    [Description("End, HH:mm. Earlier than the start means past midnight.")]
    public required string End { get; init; }

    [Description("What they did, in their words.")]
    public string? Description { get; init; }
}

/// <summary>
/// Logs the user's own work, through POST /api/work-entries once confirmed. The card says how much of it would be
/// overtime, from the same preview the log form shows; the app's own rules (edit window, overlaps, future entries) apply.
/// </summary>
public sealed class ProposeLogWorkTool(
    IProjectService projects, IWorkEntryService workEntries, IMyTimeService myTime, IClock clock,
    IUserRepository users, IProjectDepartmentRepository departments)
    : ProtonActionTool<LogWorkInput, MyWorkArgs>
{
    public override string Name => "propose_log_work";

    public override string Description =>
        "Propose logging the user's own work on a project: a day, start and end in company time, and what they did. " +
        "Nothing is logged until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.Create));

    protected override async Task<(MyWorkArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        LogWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var request = WorkText.RequestOf(clock, project.Id, input.Day ?? context.Today, input.Start, input.End, input.Description);
        var changes = new List<ProtonChange>
        {
            new("Project", null, project.Title),
            new("When", null, WorkText.When(clock, request)),
            new("Description", null, request.Description ?? "none"),
        };
        var note = await WorkText.OvertimeAsync(myTime, request, null, cancellationToken);
        note = Join(note, await DepartmentNoteAsync(project, context, cancellationToken));
        return (new MyWorkArgs(null, request, null), new ProtonPreview("Log your work", changes, note), ProjectText.Ref(project));
    }

    protected override object RequestOf(MyWorkArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(MyWorkArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = await workEntries.CreateAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Logged {WorkText.Hours(entry.Minutes)} on {entry.Project.Title}.", "work_entry", entry.Id, []);
    }

    /// <summary>
    /// On a project with departments: no department refuses, as the API would; a department the project doesn't have
    /// keeps the card, but says the hours wait until it is added.
    /// </summary>
    private async Task<string?> DepartmentNoteAsync(ProjectResponse project, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var onProject = (await departments.ListAsync([project.Id], cancellationToken)).Select(d => d.TeamId).ToList();
        if (onProject.Count == 0)
        {
            return null;
        }

        var department = (await users.GetByIdAsync(context.Access.UserId, cancellationToken))?.TeamId;
        if (department is not { } theirs)
        {
            throw ProjectDepartmentRules.NoDepartment().ToException();
        }

        return ProjectDepartmentRules.MayLog(onProject, theirs)
            ? null
            : $"Your department isn't one of {project.Title}'s, so these hours wait, not logged yet, until it is added to the project.";
    }

    private static string? Join(string? first, string? second)
        => first is null ? second : second is null ? first : $"{first} {second}";
}

public sealed record UpdateMyWorkInput : ProtonActionInput
{
    [Description("One of the user's own entries, from list_my_work.")]
    public required long WorkEntryId { get; init; }

    public long? ProjectId { get; init; }

    public DateOnly? Day { get; init; }

    [Description("HH:mm.")]
    public string? Start { get; init; }

    [Description("HH:mm.")]
    public string? End { get; init; }

    public string? Description { get; init; }
}

/// <summary>Edits one of the user's own work entries, through PUT /api/work-entries/{id} once confirmed, while it is still editable.</summary>
public sealed class ProposeUpdateMyWorkTool(
    IWorkEntryQueryService queries, IWorkEntryService workEntries, IProjectService projects, IMyTimeService myTime, IClock clock)
    : ProtonActionTool<UpdateMyWorkInput, MyWorkArgs>
{
    public override string Name => "propose_update_my_work";

    public override string Description =>
        "Propose changing one of the user's own work entries (its project, time or description) while it is still " +
        "editable. Nothing changes until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.Update));

    protected override async Task<(MyWorkArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateMyWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = await WorkText.MineAsync(queries, input.WorkEntryId, context, cancellationToken);
        var before = WorkText.RequestOf(entry);
        var start = clock.ToCompanyTime(entry.StartedAt);
        var end = clock.ToCompanyTime(entry.EndedAt);
        var after = WorkText.RequestOf(clock, input.ProjectId ?? entry.Project.Id, input.Day ?? DateOnly.FromDateTime(start),
            input.Start ?? start.ToString("HH:mm", CultureInfo.InvariantCulture),
            input.End ?? end.ToString("HH:mm", CultureInfo.InvariantCulture),
            input.Description ?? entry.Description) with { AttachmentFileId = before.AttachmentFileId };

        var changes = new List<ProtonChange>();
        if (after.ProjectId != before.ProjectId)
        {
            changes.Add(new ProtonChange("Project", entry.Project.Title, (await projects.GetAsync(after.ProjectId, cancellationToken)).Title));
        }

        if (after.StartedAt != before.StartedAt || after.EndedAt != before.EndedAt)
        {
            changes.Add(new ProtonChange("When", WorkText.When(clock, before), WorkText.When(clock, after)));
        }

        if (after.Description != before.Description)
        {
            changes.Add(new ProtonChange("Description", before.Description ?? "none", after.Description ?? "none"));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        var note = await WorkText.OvertimeAsync(myTime, after, entry.Id, cancellationToken);
        return (new MyWorkArgs(entry.Id, after, before), new ProtonPreview("Change your work entry", changes, note),
            new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object RequestOf(MyWorkArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(MyWorkArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await WorkText.MineAsync(queries, args.EntryId!.Value, context, cancellationToken);
        if (!WorkText.Same(WorkText.RequestOf(current), args.Before!))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The entry changed since Proton proposed this. Ask again.");
        }

        var entry = await workEntries.UpdateAsync(current.Id, args.Request, cancellationToken);
        return new ProtonActionDone($"Updated your entry on {entry.Project.Title}.", "work_entry", entry.Id, []);
    }
}

public sealed record DeleteMyWorkArgs(long EntryId);

public sealed record DeleteMyWorkInput : ProtonActionInput
{
    [Description("One of the user's own entries, from list_my_work.")]
    public required long WorkEntryId { get; init; }
}

/// <summary>
/// Deletes one of the user's own work entries, through DELETE /api/work-entries/{id} once confirmed. It can be restored
/// from the app within the edit window.
/// </summary>
public sealed class ProposeDeleteMyWorkTool(IWorkEntryQueryService queries, IWorkEntryService workEntries, IClock clock)
    : ProtonActionTool<DeleteMyWorkInput, DeleteMyWorkArgs>
{
    public override string Name => "propose_delete_my_work";

    public override string Description =>
        "Propose deleting one of the user's own work entries while it is still editable (it can be restored in the app " +
        "for a while). Nothing changes until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.Delete));

    protected override async Task<(DeleteMyWorkArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DeleteMyWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = await WorkText.MineAsync(queries, input.WorkEntryId, context, cancellationToken);
        return (new DeleteMyWorkArgs(entry.Id),
            new ProtonPreview("Delete your work entry",
                [new ProtonChange("Entry", $"{clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}", "deleted")],
                "You can restore it from My work while it is still editable."),
            new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object? RequestOf(DeleteMyWorkArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(DeleteMyWorkArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await workEntries.DeleteAsync(args.EntryId, cancellationToken);
        return new ProtonActionDone("Deleted the entry.", "work_entry", args.EntryId, []);
    }
}

public sealed record RestoreMyWorkArgs(long EntryId);

public sealed record RestoreMyWorkInput : ProtonActionInput
{
    [Description("One of the user's own deleted entries, from list_my_deleted_work.")]
    public required long WorkEntryId { get; init; }
}

/// <summary>
/// Brings back one of the user's own deleted work entries, through POST /api/work-entries/{id}/restore once confirmed.
/// Only an entry the app would still restore (inside the edit window, an open month, a project that takes work); the
/// app's overlap rule applies at Confirm.
/// </summary>
public sealed class ProposeRestoreMyWorkTool(IWorkEntryQueryService queries, IWorkEntryService workEntries, IMyTimeService myTime, IClock clock)
    : ProtonActionTool<RestoreMyWorkInput, RestoreMyWorkArgs>
{
    public override string Name => "propose_restore_my_work";

    public override string Description =>
        "Propose bringing back one of the user's own deleted work entries (ids from list_my_deleted_work) while it can " +
        "still be restored. Nothing changes until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.Restore));

    protected override async Task<(RestoreMyWorkArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RestoreMyWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the person's own deleted entries only: anyone else's, or one that isn't deleted, reads as not found
        var entry = (await queries.ListMyDeletedAsync(cancellationToken)).FirstOrDefault(e => e.Id == input.WorkEntryId)
            ?? throw new NotFoundException("Not one of the user's deleted entries.");
        if (!entry.CanEdit)
        {
            throw new ProtonToolInputException(
                "That entry can't be restored any more: it is past the edit window, in a closed month, or on a project that no longer takes work.");
        }

        var note = await WorkText.OvertimeAsync(myTime, WorkText.RequestOf(entry), null, cancellationToken);
        return (new RestoreMyWorkArgs(entry.Id),
            new ProtonPreview("Restore your work entry",
                [new ProtonChange("Entry", "deleted", $"{clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}")],
                note),
            new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object? RequestOf(RestoreMyWorkArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(RestoreMyWorkArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = await workEntries.RestoreAsync(args.EntryId, cancellationToken);
        return new ProtonActionDone($"Restored your entry on {entry.Project.Title}.", "work_entry", entry.Id, []);
    }
}

public sealed record SetWorkEntryScopeArgs(long EntryId, bool OutsideScope);

public sealed record SetWorkEntryScopeInput : ProtonActionInput
{
    [Description("Anyone's entry, from list_work_entries or get_work_entry.")]
    public required long WorkEntryId { get; init; }

    [Description("True to mark it as work outside the project's original scope (the client asked for more than was agreed); false to count it inside the scope again.")]
    public required bool OutsideScope { get; init; }
}

/// <summary>
/// An admin's correction of whether someone's entry is outside the project's original scope, through
/// PUT /api/work-entries/{id}/scope once confirmed: after the edit window too, but never in a closed month.
/// </summary>
public sealed class ProposeSetWorkEntryScopeTool(IWorkEntryQueryService queries, IWorkEntryService workEntries, IClock clock)
    : ProtonActionTool<SetWorkEntryScopeInput, SetWorkEntryScopeArgs>
{
    public override string Name => "propose_set_work_entry_scope";

    public override string Description =>
        "Propose marking someone's logged work entry as outside the project's original scope (work the client asked for " +
        "beyond what was agreed), or back inside it. For people who see everyone's work. Nothing changes until the user " +
        "confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.SetScope));

    protected override async Task<(SetWorkEntryScopeArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SetWorkEntryScopeInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = (await queries.GetAsync(input.WorkEntryId, cancellationToken)).Entry;
        WorkEntryRules.CheckOpenMonth(clock.CompanyDayStartUtc(WorkEntryRules.OpenFrom(clock.CompanyToday)), entry.StartedAt).ThrowIfDenied();
        if (entry.OutsideScope == input.OutsideScope)
        {
            throw new ProtonToolInputException(input.OutsideScope ? "That entry is already marked as outside the scope." : "That entry is already inside the scope.");
        }

        var changes = new List<ProtonChange>
        {
            new("Entry", null, $"{entry.Person.Name}, {clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}"),
            new("Scope", entry.OutsideScope ? "outside the original scope" : "inside the original scope", input.OutsideScope ? "outside the original scope" : "inside the original scope"),
        };
        return (new SetWorkEntryScopeArgs(entry.Id, input.OutsideScope),
            new ProtonPreview(
                input.OutsideScope ? $"Mark {entry.Person.Name}'s work as outside the scope" : $"Count {entry.Person.Name}'s work as inside the scope",
                changes,
                "The project's work page splits its hours by this. It's recorded in the activity feed."),
            new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object? RequestOf(SetWorkEntryScopeArgs args) => new SetWorkEntryScopeRequest { OutsideScope = args.OutsideScope };

    protected override async Task<ProtonActionDone> ExecuteAsync(SetWorkEntryScopeArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = await workEntries.SetScopeAsync(args.EntryId, new SetWorkEntryScopeRequest { OutsideScope = args.OutsideScope }, cancellationToken);
        return new ProtonActionDone(
            args.OutsideScope ? $"Marked the entry on {entry.Project.Title} as outside the scope." : $"Counted the entry on {entry.Project.Title} as inside the scope.",
            "work_entry", entry.Id, []);
    }
}

/// <summary>Shared times, lookups and wording of the own-work actions.</summary>
internal static class WorkText
{
    public static SaveWorkEntryRequest RequestOf(IClock clock, long projectId, DateOnly day, string start, string end, string? description)
    {
        var from = Minute(start);
        var to = Minute(end);
        var startsAt = clock.FromCompanyTime(day, from);
        // an end before the start is past midnight
        var endsAt = clock.FromCompanyTime(to <= from ? day.AddDays(1) : day, to);
        return new SaveWorkEntryRequest
        {
            ProjectId = projectId, StartedAt = startsAt, EndedAt = endsAt, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    public static SaveWorkEntryRequest RequestOf(WorkEntryResponse e)
        => new()
        {
            ProjectId = e.Project.Id, StartedAt = e.StartedAt, EndedAt = e.EndedAt, Description = e.Description, AttachmentFileId = e.Attachment?.Id,
            OutsideScope = e.OutsideScope,
        };

    /// <summary>The fields an entry is saved with (its lists of moves and deletes are only for the timeline's swaps).</summary>
    public static bool Same(SaveWorkEntryRequest a, SaveWorkEntryRequest b)
        => a.ProjectId == b.ProjectId && a.StartedAt == b.StartedAt && a.EndedAt == b.EndedAt && a.Description == b.Description
            && a.AttachmentFileId == b.AttachmentFileId && (a.OutsideScope ?? false) == (b.OutsideScope ?? false);

    private static int Minute(string time)
        => TimeOnly.TryParseExact(time.Trim(), "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.Hour * 60 + t.Minute
            : throw new ProtonToolInputException("Times are HH:mm, for example 09:30.");

    public static string When(IClock clock, SaveWorkEntryRequest r)
    {
        var start = clock.ToCompanyTime(r.StartedAt);
        var end = clock.ToCompanyTime(r.EndedAt);
        var minutes = (int)(r.EndedAt - r.StartedAt).TotalMinutes;
        return $"{start.ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)}–{end.ToString("HH:mm", CultureInfo.InvariantCulture)} ({Hours(minutes)})";
    }

    public static string Hours(int minutes) => minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";

    /// <summary>One of the user's own entries, still editable; anyone else's reads as not found.</summary>
    public static async Task<WorkEntryResponse> MineAsync(IWorkEntryQueryService queries, long id, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = (await queries.GetAsync(id, cancellationToken)).Entry;
        if (entry.Person.Id != context.Access.UserId)
        {
            throw new NotFoundException("Not one of the user's own entries.");
        }

        return entry.CanEdit ? entry : throw new ProtonToolInputException("That entry can no longer be edited: it is past the edit window.");
    }

    /// <summary>The overtime the log form would warn about, in words; null when there is none.</summary>
    public static async Task<string?> OvertimeAsync(IMyTimeService myTime, SaveWorkEntryRequest request, long? entryId, CancellationToken cancellationToken)
    {
        var preview = await myTime.PreviewOvertimeAsync(
            new OvertimePreviewRequest { StartedAt = request.StartedAt, EndedAt = request.EndedAt, EntryId = entryId }, cancellationToken);
        if (preview.OvertimeMinutes == 0)
        {
            return null;
        }

        return preview.DayOff
            ? $"It's a day off for you, so all {Hours(preview.OvertimeMinutes)} count as overtime."
            : $"{Hours(preview.OvertimeMinutes)} of it count as overtime.";
    }
}
