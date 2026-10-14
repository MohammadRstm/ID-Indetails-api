using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>
/// A completed project qualifies when its completion, its latest activity event (invoices, payments, edits…) and its
/// latest client portal message or comment are all at least 90 days old. Archiving it is a system event: no actor, and
/// no notification, as archiving never notifies.
/// </summary>
public sealed class ProjectAutoArchive(
    IProjectRepository projects,
    IDomainSettingsService domainSettings,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectAutoArchive
{
    public const int QuietDays = 90;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var archived = 0;
        foreach (var domain in DomainCodes.All)
        {
            if (!(await domainSettings.CurrentAsync(domain, cancellationToken)).AutoArchive)
            {
                continue;
            }

            foreach (var id in await projects.QuietCompletedAsync(domain, now.AddDays(-QuietDays), cancellationToken))
            {
                await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
                var project = await projects.GetForUpdateAsync(id, cancellationToken);
                if (project?.Status != ProjectStatus.Completed)
                {
                    // Reopened or archived meanwhile.
                    continue;
                }

                project.Status = ProjectStatus.Archived;
                project.UpdatedAt = now;
                activity.Add(new ActivityEvent
                {
                    DomainCode = project.DomainCode,
                    Kind = ActivityEventKinds.ProjectArchived,
                    OccurredAt = now,
                    ProjectId = project.Id,
                    SubjectType = "project",
                    SubjectId = project.Id,
                    Payload = JsonSerializer.Serialize(new { automatic = true }),
                });
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                archived++;
            }
        }

        return archived;
    }
}
