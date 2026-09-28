using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// Which status changes are allowed, and what each one does:
/// <code>
/// active    → hold, completed, archived
/// hold      → active (resume), completed, archived
/// completed → active (reopen), archived
/// archived  → active (reopen)
/// </code>
/// Completing, or archiving a project that isn't completed, <b>finalises</b> it: revenue is locked
/// (Execution's agreed amount is frozen) and open requirements are ticked as done automatically.
/// Reopening undoes both. A change to the current status is refused.
/// </summary>
public static class ProjectStatusRules
{
    public enum Action
    {
        Hold,
        Resume,
        Complete,
        Archive,
        Reopen,
    }

    public sealed record Transition(ProjectStatus To, bool Finalises, bool Reopens, string EventKind);

    public static (Transition? Transition, RuleDenial? Denial) Plan(ProjectStatus from, Action action)
    {
        var (to, allowedFrom, kind) = Rule(action);

        if (from == to)
        {
            return (null, RuleDenial.BusinessRule(ErrorCodes.StatusUnchanged, $"The project is already {Describe(to)}."));
        }

        if (!allowedFrom.Contains(from))
        {
            return (null, RuleDenial.BusinessRule(
                ErrorCodes.InvalidStatusChange, $"A project that is {Describe(from)} can't be {Verb(action)}."));
        }

        var finalises = to == ProjectStatus.Completed || (to == ProjectStatus.Archived && from != ProjectStatus.Completed);
        return (new Transition(to, finalises, action == Action.Reopen, kind), null);
    }

    private static (ProjectStatus To, ProjectStatus[] AllowedFrom, string EventKind) Rule(Action action) => action switch
    {
        Action.Hold => (ProjectStatus.Hold, [ProjectStatus.Active], ActivityEventKinds.ProjectHold),
        Action.Resume => (ProjectStatus.Active, [ProjectStatus.Hold], ActivityEventKinds.ProjectResumed),
        Action.Complete => (ProjectStatus.Completed, [ProjectStatus.Active, ProjectStatus.Hold], ActivityEventKinds.ProjectCompleted),
        Action.Archive => (ProjectStatus.Archived, [ProjectStatus.Active, ProjectStatus.Hold, ProjectStatus.Completed], ActivityEventKinds.ProjectArchived),
        _ => (ProjectStatus.Active, [ProjectStatus.Completed, ProjectStatus.Archived], ActivityEventKinds.ProjectReopened),
    };

    private static string Describe(ProjectStatus status) => status switch
    {
        ProjectStatus.Hold => "on hold",
        ProjectStatus.Completed => "completed",
        ProjectStatus.Archived => "archived",
        _ => "active",
    };

    private static string Verb(Action action) => action switch
    {
        Action.Hold => "put on hold",
        Action.Resume => "resumed",
        Action.Complete => "completed",
        Action.Archive => "archived",
        _ => "reopened",
    };
}
