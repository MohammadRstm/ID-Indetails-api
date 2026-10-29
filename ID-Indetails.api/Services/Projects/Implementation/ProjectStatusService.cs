using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>Status changes, following <see cref="ProjectStatusRules"/>.</summary>
public sealed class ProjectStatusService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IRequirementRepository requirements,
    IProjectProgress progress,
    IProjectService projectService,
    IProjectFinanceService finance,
    IActivityEventRepository activity,
    IProjectNotifier notifier,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectStatusService
{
    public async Task<ProjectResponse> ChangeAsync(
        long projectId,
        ProjectStatusRules.Action action,
        CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        var (transition, denial) = ProjectStatusRules.Plan(project.Status, action);
        if (denial is not null)
        {
            throw denial.ToException();
        }

        var now = clock.UtcNow;
        project.Status = transition!.To;
        project.UpdatedAt = now;
        activity.Add(Event(project, transition.EventKind, actor.UserId, null));

        if (transition.Finalises)
        {
            await FinaliseAsync(project, actor.UserId, now, cancellationToken);
            await notifier.ClosedAsync(project, (await finance.GetAsync(project, cancellationToken)).Summary.Margin, actor.UserId, cancellationToken);
        }

        if (transition.Reopens)
        {
            project.CompletedAt = null;
            project.FinalizedAt = null;
            project.RevenueLockedAt = null;
            if (project.DomainCode == DomainCodes.Execution)
            {
                // Open Execution projects calculate their agreed amount live again.
                project.Revenue = 0;
            }

            await requirements.UntickAutoDoneAsync(project.Id, cancellationToken);
        }

        await progress.RefreshAsync(project, actor.UserId, cancellationToken);
        await notifier.StatusChangedAsync(project, action, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await projectService.GetAsync(project.Id, cancellationToken);
    }

    /// <summary>Locks revenue (freezing Execution's agreed amount) and ticks open requirements as done automatically.</summary>
    private async Task FinaliseAsync(Project project, long actorId, DateTime now, CancellationToken cancellationToken)
    {
        if (project.DomainCode == DomainCodes.Execution)
        {
            // The agreed amount, the work price plus supervision, is frozen here.
            var money = await finance.GetAsync(project, cancellationToken);
            project.Revenue = money.Summary.Revenue;
        }

        project.CompletedAt = now;
        project.FinalizedAt = now;
        project.RevenueLockedAt = now;
        await requirements.AutoTickOpenAsync(project.Id, now, actorId, cancellationToken);
        activity.Add(Event(project, ActivityEventKinds.ProjectFinalized, actorId, project.Revenue));
    }

    private ActivityEvent Event(Project project, string kind, long actorId, decimal? amount) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "project",
        SubjectId = project.Id,
        Amount = amount,
    };
}
