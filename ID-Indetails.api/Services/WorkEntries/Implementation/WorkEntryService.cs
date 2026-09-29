using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// People log only their own work, and only on Design projects (see <see cref="WorkEntryRules"/>).
/// Someone else's entry reads as not found. Each change locks the person's user row, so overlap
/// checks of two simultaneous changes run one after the other. Work in a closed month never changes.
/// </summary>
public sealed class WorkEntryService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IWorkEntryRepository entries,
    IDomainSettingsService domainSettings,
    IWorkLogNotifier notifier,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IOvertimeService overtime,
    IClock clock) : IWorkEntryService
{
    public async Task<WorkEntryResponse> CreateAsync(SaveWorkEntryRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await entries.LockPersonAsync(actor.UserId, cancellationToken);

        var project = await LoggableProjectAsync(actor, request.ProjectId, settings, cancellationToken);
        var entry = new WorkEntry
        {
            UserId = actor.UserId,
            ProjectId = project.Id,
            Description = request.Description?.Trim() ?? string.Empty,
            StartedAt = WorkEntryRules.AsUtc(request.StartedAt),
            EndedAt = WorkEntryRules.AsUtc(request.EndedAt),
            CreatedAt = clock.UtcNow,
        };
        Throw(WorkEntryRules.CheckEntry(entry.Description, entry.EndedAt, clock.UtcNow, settings.RequireDescription, settings.AllowFutureEntries));
        Throw(WorkEntryRules.CheckOpenMonth(WorkEntryMapper.MonthStartUtc(clock), entry.StartedAt));

        var moved = await ApplyMovesAsync(actor, request.Moves, settings, cancellationToken);
        await CheckOverlapAsync(actor.UserId, [new WorkEntryRules.Span(0, entry.StartedAt, entry.EndedAt, project.Title), .. moved.Select(SpanOf)], settings, cancellationToken);

        entries.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(EntryEvent(entry, ActivityEventKinds.WorkEntryLogged, actor.UserId));
        AddMoveEvents(moved, actor.UserId);
        await notifier.NotifyAsync(entry, project.Title, currentUser.FullName, settings.NotifyEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(entry.Id, actor, settings, cancellationToken);
    }

    public async Task<WorkEntryResponse> UpdateAsync(long id, SaveWorkEntryRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await entries.LockPersonAsync(actor.UserId, cancellationToken);

        var entry = Own(actor, await entries.GetForUpdateAsync(id, cancellationToken));
        Throw(WorkEntryRules.CheckOpenMonth(WorkEntryMapper.MonthStartUtc(clock), entry.StartedAt, WorkEntryRules.AsUtc(request.StartedAt)));
        Throw(WorkEntryRules.CheckChangeable(entry.CreatedAt, clock.UtcNow, settings.EditWindow, entry.Project.Status, settings.HoldLogging));
        var project = entry.ProjectId == request.ProjectId
            ? entry.Project
            : await LoggableProjectAsync(actor, request.ProjectId, settings, cancellationToken);

        entry.ProjectId = project.Id;
        entry.Description = request.Description?.Trim() ?? string.Empty;
        entry.StartedAt = WorkEntryRules.AsUtc(request.StartedAt);
        entry.EndedAt = WorkEntryRules.AsUtc(request.EndedAt);
        entry.UpdatedAt = clock.UtcNow;
        Throw(WorkEntryRules.CheckEntry(entry.Description, entry.EndedAt, clock.UtcNow, settings.RequireDescription, settings.AllowFutureEntries));

        var moved = await ApplyMovesAsync(actor, request.Moves.Where(m => m.Id != entry.Id).ToList(), settings, cancellationToken);
        await CheckOverlapAsync(actor.UserId, [new WorkEntryRules.Span(entry.Id, entry.StartedAt, entry.EndedAt, project.Title), .. moved.Select(SpanOf)], settings, cancellationToken);

        activity.Add(EntryEvent(entry, ActivityEventKinds.WorkEntryUpdated, actor.UserId));
        AddMoveEvents(moved, actor.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(entry.Id, actor, settings, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var entry = Own(actor, await entries.GetForUpdateAsync(id, cancellationToken));
        Throw(WorkEntryRules.CheckOpenMonth(WorkEntryMapper.MonthStartUtc(clock), entry.StartedAt));
        Throw(WorkEntryRules.CheckChangeable(entry.CreatedAt, clock.UtcNow, settings.EditWindow, entry.Project.Status, settings.HoldLogging));

        entry.DeletedAt = clock.UtcNow;
        activity.Add(EntryEvent(entry, ActivityEventKinds.WorkEntryDeleted, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WorkEntryResponse> RestoreAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await entries.LockPersonAsync(actor.UserId, cancellationToken);

        var entry = Own(actor, await entries.GetDeletedForUpdateAsync(id, cancellationToken));
        Throw(WorkEntryRules.CheckOpenMonth(WorkEntryMapper.MonthStartUtc(clock), entry.StartedAt));
        Throw(WorkEntryRules.CheckChangeable(entry.CreatedAt, clock.UtcNow, settings.EditWindow, entry.Project.Status, settings.HoldLogging));
        await CheckOverlapAsync(actor.UserId, [SpanOf(entry)], settings, cancellationToken);

        entry.DeletedAt = null;
        activity.Add(EntryEvent(entry, ActivityEventKinds.WorkEntryRestored, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(entry.Id, actor, settings, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>A project the caller can see, of the Design domain, that accepts work right now.</summary>
    private async Task<Project> LoggableProjectAsync(UserAccess actor, long projectId, DomainSettings settings, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(WorkEntryRules.CheckProject(project.DomainCode, project.Status, settings.HoldLogging));
        return project;
    }

    /// <summary>Other people's entries read as not found, whatever the caller may view.</summary>
    private static WorkEntry Own(UserAccess actor, WorkEntry? entry)
        => entry is not null && entry.UserId == actor.UserId ? entry : throw new NotFoundException("Work entry not found.");

    /// <summary>Moves the caller's other entries to their new times.</summary>
    private async Task<IReadOnlyList<WorkEntry>> ApplyMovesAsync(
        UserAccess actor,
        IReadOnlyList<WorkEntryMoveRequest> moves,
        DomainSettings settings,
        CancellationToken cancellationToken)
    {
        if (moves.Count == 0)
        {
            return [];
        }

        var tracked = await entries.ListOwnForUpdateAsync(actor.UserId, moves.Select(m => m.Id).ToList(), cancellationToken);
        foreach (var move in moves)
        {
            var entry = tracked.SingleOrDefault(e => e.Id == move.Id) ?? throw new NotFoundException("Work entry not found.");
            Throw(WorkEntryRules.CheckOpenMonth(WorkEntryMapper.MonthStartUtc(clock), entry.StartedAt, WorkEntryRules.AsUtc(move.StartedAt)));
            Throw(WorkEntryRules.CheckChangeable(entry.CreatedAt, clock.UtcNow, settings.EditWindow, entry.Project.Status, settings.HoldLogging));

            entry.StartedAt = WorkEntryRules.AsUtc(move.StartedAt);
            entry.EndedAt = WorkEntryRules.AsUtc(move.EndedAt);
            entry.UpdatedAt = clock.UtcNow;
            Throw(WorkEntryRules.CheckEntry(entry.Description, entry.EndedAt, clock.UtcNow, requireDescription: false, settings.AllowFutureEntries));
        }

        return tracked;
    }

    private async Task CheckOverlapAsync(
        long userId,
        IReadOnlyList<WorkEntryRules.Span> changed,
        DomainSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.AllowOverlap)
        {
            return;
        }

        var others = await entries.SpansAsync(userId, changed.Min(s => s.Start), changed.Max(s => s.End), cancellationToken);
        Throw(WorkEntryRules.CheckOverlap(changed, others));
    }

    private void AddMoveEvents(IEnumerable<WorkEntry> moved, long actorId)
    {
        foreach (var entry in moved)
        {
            activity.Add(EntryEvent(entry, ActivityEventKinds.WorkEntryUpdated, actorId));
        }
    }

    private static WorkEntryRules.Span SpanOf(WorkEntry entry) => new(entry.Id, entry.StartedAt, entry.EndedAt, entry.Project.Title);

    private async Task<WorkEntryResponse> DescribeAsync(long id, UserAccess actor, DomainSettings settings, CancellationToken cancellationToken)
    {
        var view = await entries.GetViewAsync(id, cancellationToken) ?? throw new NotFoundException("Work entry not found.");
        return WorkEntryMapper.ToResponse(view, actor.UserId, settings, clock, await overtime.OvertimeOfAsync([view], cancellationToken));
    }

    private ActivityEvent EntryEvent(WorkEntry entry, string kind, long actorId) => new()
    {
        DomainCode = DomainCodes.Design,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = entry.ProjectId,
        SubjectType = "work_entry",
        SubjectId = entry.Id,
        Payload = JsonSerializer.Serialize(new { minutes = (int)(entry.EndedAt - entry.StartedAt).TotalMinutes }),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
