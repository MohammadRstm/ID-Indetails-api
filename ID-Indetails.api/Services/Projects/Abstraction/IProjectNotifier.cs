using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>Tells people about projects and their requirements. Stages the notifications in the caller's unit of work.</summary>
public interface IProjectNotifier
{
    Task CreatedAsync(Project project, long actorId, CancellationToken cancellationToken = default);

    /// <summary>Hold, resume, complete and reopen notify; archiving doesn't.</summary>
    Task StatusChangedAsync(Project project, ProjectStatusRules.Action action, long actorId, CancellationToken cancellationToken = default);

    Task OwnerSetAsync(Project project, long actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Once a finalised project's margin is known (client projects with revenue), its margin as a percentage to
    /// whoever sees the domain's money.
    /// </summary>
    Task ClosedAsync(Project project, decimal? margin, long actorId, CancellationToken cancellationToken = default);

    /// <summary>Notifies once, when an active project's progress crosses 90%.</summary>
    Task ProgressChangedAsync(Project project, int previousPct, long actorId, CancellationToken cancellationToken = default);

    Task RequirementAddedAsync(Project project, ProjectRequirement requirement, long actorId, CancellationToken cancellationToken = default);

    Task RequirementDoneAsync(Project project, ProjectRequirement requirement, long actorId, CancellationToken cancellationToken = default);
}
