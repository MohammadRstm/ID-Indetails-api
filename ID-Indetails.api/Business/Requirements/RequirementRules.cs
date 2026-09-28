using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Requirements;

/// <summary>
/// <list type="bullet">
/// <item>Requirements of a completed or archived project can't change until it is reopened.</item>
/// <item>Assignees are employees (not admins) of the project's domain who are active or invited.
/// They don't have to be project members.</item>
/// <item>A requirement is overdue when it is open and its deadline is before today.</item>
/// </list>
/// </summary>
public static class RequirementRules
{
    public sealed record Person(long Id, string Role, string? Domain, UserStatus Status);

    public static RuleDenial? CheckEditable(ProjectStatus status)
        => status is ProjectStatus.Completed or ProjectStatus.Archived
            ? RuleDenial.BusinessRule(ErrorCodes.ProjectClosed, "This project is finished. Reopen it to change its requirements.")
            : null;

    /// <param name="found">The people found for <paramref name="requestedIds"/>; missing ids are simply absent.</param>
    public static RuleDenial? CheckAssignees(IReadOnlyCollection<long> requestedIds, IReadOnlyCollection<Person> found, string projectDomain)
    {
        var allowed = found
            .Where(p => p.Role == RoleCodes.Employee && p.Domain == projectDomain && p.Status != UserStatus.Deactivated)
            .Select(p => p.Id)
            .ToHashSet();

        return requestedIds.All(allowed.Contains)
            ? null
            : RuleDenial.BusinessRule(
                ErrorCodes.AssigneeNotAllowed, "Requirements can only be assigned to active or invited employees of the project's domain.");
    }

    public static bool IsOverdue(DateOnly? deadline, bool isDone, DateOnly today)
        => !isDone && deadline is { } d && d < today;
}
