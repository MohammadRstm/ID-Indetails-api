using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A project is overdue when it has a target date, that date is before today (company time zone), and it is still
/// active or on hold. A project without a target date is never overdue: no expected end is assumed.
/// </summary>
public static class ProjectOverdue
{
    public static bool IsOverdue(DateOnly? targetDate, ProjectStatus status, DateOnly today)
        => targetDate is { } target
            && target < today
            && status is ProjectStatus.Active or ProjectStatus.Hold;

    /// <summary>Whole days past the target date, or null when not overdue.</summary>
    public static int? DaysOverdue(DateOnly? targetDate, ProjectStatus status, DateOnly today)
        => IsOverdue(targetDate, status, today) ? today.DayNumber - targetDate!.Value.DayNumber : null;
}
