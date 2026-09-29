using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>
/// Project news (added, status changes, requirements added) goes to the domain's own people while the domain's
/// <c>notify_project</c> setting is on. Owner, progress and ticked-requirement notifications always go.
/// </summary>
public sealed class ProjectNotifier(
    INotificationSender sender,
    IDomainSettingsService domainSettings,
    IClock clock) : IProjectNotifier
{
    public const int NearlyDonePct = 90;

    public async Task CreatedAsync(Project project, long actorId, CancellationToken cancellationToken = default)
    {
        if (await NotifyProjectAsync(project, cancellationToken))
        {
            await sender.SendAsync(
                Draft(NotificationKinds.ProjectCreated, project, actorId, "project", project.Id, new { projectTitle = project.Title }),
                await sender.DomainMembersAsync(project.DomainCode, cancellationToken),
                cancellationToken);
        }
    }

    public async Task StatusChangedAsync(
        Project project,
        ProjectStatusRules.Action action,
        long actorId,
        CancellationToken cancellationToken = default)
    {
        if (action == ProjectStatusRules.Action.Archive || !await NotifyProjectAsync(project, cancellationToken))
        {
            return;
        }

        var change = action.ToString().ToLowerInvariant();
        await sender.SendAsync(
            Draft(NotificationKinds.ProjectStatusChanged, project, actorId, "project", project.Id, new { projectTitle = project.Title, change }),
            await sender.DomainMembersAsync(project.DomainCode, cancellationToken),
            cancellationToken);
    }

    public Task OwnerSetAsync(Project project, long actorId, CancellationToken cancellationToken = default)
        => sender.SendAsync(
            Draft(NotificationKinds.ProjectOwnerSet, project, actorId, "project", project.Id, new { projectTitle = project.Title }),
            [project.OwnerId],
            cancellationToken);

    public async Task ClosedAsync(Project project, decimal? margin, long actorId, CancellationToken cancellationToken = default)
    {
        if (margin is not { } closedAt)
        {
            return;
        }

        await sender.SendAsync(
            Draft(NotificationKinds.ProjectClosed, project, actorId, "project", project.Id,
                new { projectTitle = project.Title, marginPct = Math.Round(closedAt * 100, 1, MidpointRounding.AwayFromZero) }),
            await sender.HoldersAsync(project.DomainCode, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
    }

    public async Task ProgressChangedAsync(Project project, int previousPct, long actorId, CancellationToken cancellationToken = default)
    {
        if (project.Status != ProjectStatus.Active || previousPct >= NearlyDonePct || project.ProgressPct < NearlyDonePct)
        {
            return;
        }

        var recipients = (await sender.HoldersAsync(project.DomainCode, PermissionCodes.ProjectChangeStatus, cancellationToken))
            .Append(project.OwnerId);
        await sender.SendAsync(
            Draft(NotificationKinds.ProjectNearlyDone, project, actorId, "project", project.Id,
                new { projectTitle = project.Title, progressPct = project.ProgressPct }),
            recipients,
            cancellationToken);
    }

    public async Task RequirementAddedAsync(
        Project project,
        ProjectRequirement requirement,
        long actorId,
        CancellationToken cancellationToken = default)
    {
        if (!await NotifyProjectAsync(project, cancellationToken))
        {
            return;
        }

        // The assignees already get requirement_assigned.
        var assignees = requirement.Assignees.Select(a => a.UserId).ToHashSet();
        var recipients = (await sender.DomainMembersAsync(project.DomainCode, cancellationToken)).Where(id => !assignees.Contains(id));
        await sender.SendDigestAsync(
            Draft(NotificationKinds.RequirementsAdded, project, actorId, "requirement", requirement.Id,
                new { projectTitle = project.Title, latestText = requirement.Text }),
            $"requirements-added-{project.Id}-{clock.CompanyToday:yyyy-MM-dd}",
            recipients,
            cancellationToken);
    }

    public async Task RequirementDoneAsync(
        Project project,
        ProjectRequirement requirement,
        long actorId,
        CancellationToken cancellationToken = default)
    {
        var recipients = (await sender.HoldersAsync(project.DomainCode, PermissionCodes.RequirementManage, cancellationToken))
            .Concat(requirement.Assignees.Select(a => a.UserId));
        await sender.SendAsync(
            Draft(NotificationKinds.RequirementDone, project, actorId, "requirement", requirement.Id,
                new { projectTitle = project.Title, text = requirement.Text }),
            recipients,
            cancellationToken);
    }

    private async Task<bool> NotifyProjectAsync(Project project, CancellationToken cancellationToken)
        => (await domainSettings.CurrentAsync(project.DomainCode, cancellationToken)).NotifyProject;

    private static NotificationDraft Draft(string kind, Project project, long actorId, string subjectType, long subjectId, object payload)
        => new(kind, actorId, project.DomainCode, project.Id, subjectType, subjectId, payload);
}
