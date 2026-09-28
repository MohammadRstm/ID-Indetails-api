using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;

namespace ID_Indetails.api.Services.Requirements.Implementation;

/// <summary>
/// Requirements of projects in the caller's visible domains. Every change that affects progress
/// refreshes the project's cached progress in the same transaction.
/// </summary>
public sealed class RequirementService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IRequirementRepository requirements,
    IProjectProgress progress,
    IActivityEventRepository activity,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequirementService
{
    public async Task<RequirementListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        var rows = await requirements.ListAsync(projectId, cancellationToken);
        var items = await DescribeAsync(rows, cancellationToken);
        return new RequirementListResponse(
            items,
            items.Count,
            items.Count(r => r.IsDone),
            items.Count(r => r.IsOverdue),
            project.ProgressPct);
    }

    public async Task<RequirementResponse> CreateAsync(long projectId, SaveRequirementRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        var assignees = await CheckAssigneesAsync(request.AssigneeIds, project.DomainCode, cancellationToken);

        var requirement = new ProjectRequirement
        {
            ProjectId = project.Id,
            Text = request.Text.Trim(),
            Deadline = request.Deadline,
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
            Assignees = assignees.Select(id => new RequirementAssignee { UserId = id }).ToList(),
        };
        requirements.Add(requirement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(RequirementEvent(project, requirement, ActivityEventKinds.RequirementAdded, actor.UserId));
        NotifyAssigned(project, requirement, assignees, actor.UserId);
        await progress.RefreshAsync(project, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeOneAsync(projectId, requirement.Id, cancellationToken);
    }

    public async Task<RequirementResponse> UpdateAsync(
        long projectId,
        long requirementId,
        SaveRequirementRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        var requirement = await RequirementForUpdateAsync(projectId, requirementId, cancellationToken);
        var assignees = await CheckAssigneesAsync(request.AssigneeIds, project.DomainCode, cancellationToken);

        requirement.Text = request.Text.Trim();
        requirement.Deadline = request.Deadline;
        foreach (var removed in requirement.Assignees.Where(a => !assignees.Contains(a.UserId)).ToList())
        {
            requirement.Assignees.Remove(removed);
        }

        var added = assignees.Where(id => requirement.Assignees.All(a => a.UserId != id)).ToList();
        foreach (var userId in added)
        {
            requirement.Assignees.Add(new RequirementAssignee { UserId = userId });
        }

        activity.Add(RequirementEvent(project, requirement, ActivityEventKinds.RequirementUpdated, actor.UserId));
        NotifyAssigned(project, requirement, added, actor.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeOneAsync(projectId, requirement.Id, cancellationToken);
    }

    public async Task<RequirementResponse> SetDoneAsync(
        long projectId,
        long requirementId,
        bool done,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        var requirement = await RequirementForUpdateAsync(projectId, requirementId, cancellationToken);

        if (requirement.IsDone != done)
        {
            requirement.IsDone = done;
            requirement.AutoDone = false;
            requirement.DoneAt = done ? clock.UtcNow : null;
            requirement.DoneById = done ? actor.UserId : null;
            activity.Add(RequirementEvent(
                project, requirement, done ? ActivityEventKinds.RequirementDone : ActivityEventKinds.RequirementReopened, actor.UserId));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await progress.RefreshAsync(project, actor.UserId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeOneAsync(projectId, requirement.Id, cancellationToken);
    }

    public async Task DeleteAsync(long projectId, long requirementId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        var requirement = await RequirementForUpdateAsync(projectId, requirementId, cancellationToken);

        requirement.DeletedAt = clock.UtcNow;
        activity.Add(RequirementEvent(project, requirement, ActivityEventKinds.RequirementDeleted, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await progress.RefreshAsync(project, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>The project, tracked and locked, if the caller can see it and it is still open for changes.</summary>
    private async Task<Project> EditableProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(RequirementRules.CheckEditable(project.Status));
        return project;
    }

    private async Task<ProjectRequirement> RequirementForUpdateAsync(long projectId, long requirementId, CancellationToken cancellationToken)
        => await requirements.GetForUpdateAsync(projectId, requirementId, cancellationToken)
            ?? throw new NotFoundException("Requirement not found.");

    private async Task<IReadOnlyList<long>> CheckAssigneesAsync(IReadOnlyList<long> requested, string projectDomain, CancellationToken cancellationToken)
    {
        var ids = requested.Distinct().ToList();
        if (ids.Count > 0)
        {
            Throw(RequirementRules.CheckAssignees(ids, await requirements.GetPeopleAsync(ids, cancellationToken), projectDomain));
        }

        return ids;
    }

    private async Task<RequirementResponse> DescribeOneAsync(long projectId, long requirementId, CancellationToken cancellationToken)
    {
        var rows = await requirements.ListAsync(projectId, cancellationToken);
        return (await DescribeAsync(rows.Where(r => r.Id == requirementId).ToList(), cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<RequirementResponse>> DescribeAsync(
        IReadOnlyList<ProjectRequirement> rows,
        CancellationToken cancellationToken)
    {
        var doneBy = rows.Where(r => r.DoneById is not null).Select(r => r.DoneById!.Value).Distinct().ToList();
        var names = doneBy.Count > 0 ? await requirements.GetNamesAsync(doneBy, cancellationToken) : new Dictionary<long, string>();
        var today = clock.CompanyToday;

        return rows.Select(r => new RequirementResponse
        {
            Id = r.Id,
            Text = r.Text,
            Deadline = r.Deadline,
            IsOverdue = RequirementRules.IsOverdue(r.Deadline, r.IsDone, today),
            IsDone = r.IsDone,
            DoneAt = r.DoneAt,
            DoneBy = r.DoneById is { } id && names.TryGetValue(id, out var name) ? new RequirementPersonResponse(id, name) : null,
            AutoDone = r.AutoDone,
            Assignees = r.Assignees
                .Select(a => new RequirementPersonResponse(a.UserId, NameOf(a.User)))
                .OrderBy(a => a.Name)
                .ToList(),
            CreatedAt = r.CreatedAt,
        }).ToList();
    }

    /// <summary>Tells each newly assigned person (other than the caller) about the requirement.</summary>
    private void NotifyAssigned(Project project, ProjectRequirement requirement, IEnumerable<long> userIds, long actorId)
    {
        var payload = JsonSerializer.Serialize(new { text = requirement.Text, deadline = requirement.Deadline, projectTitle = project.Title });
        foreach (var userId in userIds.Where(id => id != actorId))
        {
            notifications.Add(new Notification
            {
                RecipientId = userId,
                Kind = NotificationKinds.RequirementAssigned,
                ActorId = actorId,
                ProjectId = project.Id,
                SubjectType = "requirement",
                SubjectId = requirement.Id,
                Payload = payload,
                CreatedAt = clock.UtcNow,
            });
        }
    }

    private ActivityEvent RequirementEvent(Project project, ProjectRequirement requirement, string kind, long actorId) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "requirement",
        SubjectId = requirement.Id,
        Payload = JsonSerializer.Serialize(new { text = requirement.Text }),
    };

    private static string NameOf(User user)
        => $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email;

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
