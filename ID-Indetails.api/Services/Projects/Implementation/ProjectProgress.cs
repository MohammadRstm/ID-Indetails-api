using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

public sealed class ProjectProgress(
    IRequirementRepository requirements,
    IActivityEventRepository activity,
    IProjectNotifier notifier,
    IClock clock) : IProjectProgress
{
    public async Task RefreshAsync(Project project, long actorId, CancellationToken cancellationToken = default)
    {
        var (total, done) = await requirements.CountAsync(project.Id, cancellationToken);
        var next = ProgressCalculator.Percent(total, done, project.Status);
        if (next == project.ProgressPct)
        {
            return;
        }

        activity.Add(new ActivityEvent
        {
            DomainCode = project.DomainCode,
            Kind = ActivityEventKinds.ProgressSet,
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            ProjectId = project.Id,
            SubjectType = "project",
            SubjectId = project.Id,
            Amount = next,
            PrevAmount = project.ProgressPct,
        });
        var previous = project.ProgressPct;
        project.ProgressPct = next;
        await notifier.ProgressChangedAsync(project, previous, actorId, cancellationToken);
    }
}
