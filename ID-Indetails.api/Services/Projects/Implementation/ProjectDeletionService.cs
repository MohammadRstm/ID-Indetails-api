using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>Deletes a flake project, following <see cref="ProjectDeletionRules"/>.</summary>
public sealed class ProjectDeletionService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectDeletionRepository deletion,
    IActivityEventRepository activity,
    IContentStore store,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<ProjectDeletionService> logger) : IProjectDeletionService
{
    public async Task<ProjectDeletionResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleAsync(projectId, cancellationToken);
        var blockers = await BlockersAsync(project, cancellationToken);
        return new ProjectDeletionResponse(blockers.Count == 0, blockers, await deletion.RemovesAsync(project.Id, cancellationToken));
    }

    public async Task DeleteAsync(long projectId, string? typedTitle, CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleAsync(projectId, cancellationToken, forUpdate: true);
        if (ProjectDeletionRules.CheckConfirmed(project.Title, typedTitle) is { } unconfirmed)
        {
            throw unconfirmed.ToException();
        }

        var blockers = await BlockersAsync(project, cancellationToken);
        if (blockers.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.ProjectNotDeletable, "This project can't be deleted: " + string.Join(" ", blockers));
        }

        var removed = await deletion.RemovesAsync(project.Id, cancellationToken);
        var keys = await deletion.DeleteAsync(project.Id, cancellationToken);
        // the one trace it leaves: who deleted which project, and what it held
        activity.Add(new ActivityEvent
        {
            DomainCode = project.DomainCode,
            Kind = ActivityEventKinds.ProjectDeleted,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            ClientId = project.ClientId,
            SubjectType = "project",
            SubjectId = project.Id,
            Payload = JsonSerializer.Serialize(new { title = project.Title, removed }),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // its client portal's files, once nothing points at them; the weekly check removes any left behind
        foreach (var key in keys)
        {
            try
            {
                await store.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A deleted project's stored file {Key} wasn't removed; the weekly check will.", key);
            }
        }
    }

    private async Task<IReadOnlyList<string>> BlockersAsync(Project project, CancellationToken cancellationToken)
        => ProjectDeletionRules.Blockers(
            project.Status, await deletion.BlockingAsync(project.Id, WorkEntryMapper.OpenFromUtc(clock), cancellationToken));

    private async Task<Project> VisibleAsync(long projectId, CancellationToken cancellationToken, bool forUpdate = false)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var project = forUpdate ? await projects.GetForUpdateAsync(projectId, cancellationToken) : await projects.GetAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException("Project not found.");
    }
}
