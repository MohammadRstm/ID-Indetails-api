using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A Design project's departments: who works on it and each one's share of the price. Shares are fractions that add up
/// to exactly 1; adding or removing a department puts them back to equal. Hours are logged only by people whose
/// department is on the project, and a department with hours on it stays.
/// </summary>
public static class ProjectDepartmentRules
{
    public const int MaxReasonLength = 500;

    /// <summary>The name of a project worked on by every department (two or more).</summary>
    public const string FullFile = "Full file";

    /// <summary>A department being put on a project.</summary>
    public sealed record Candidate(long Id, string Name, string Domain);

    /// <summary>
    /// Equal shares, to four places, adding up to exactly 1: what's left after rounding down goes to the first ones,
    /// 0.0001 each (three departments get 0.3334, 0.3333, 0.3333).
    /// </summary>
    public static IReadOnlyList<decimal> EqualShares(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var each = Math.Floor(10000m / count) / 10000m;
        var left = (int)((1m - each * count) * 10000m);
        return Enumerable.Range(0, count).Select(i => i < left ? each + 0.0001m : each).ToList();
    }

    /// <summary>
    /// The departments chosen for a project: Design only, each a Design department and chosen once. None is allowed: the
    /// project then works as before departments (the app asks for them when a client project is made).
    /// </summary>
    public static RuleDenial? CheckChosen(string domain, IReadOnlyList<long> chosen, IReadOnlyCollection<Candidate> found)
    {
        if (chosen.Count == 0)
        {
            return null;
        }

        if (domain != DomainCodes.Design)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "departmentIds", "Only Design projects have departments.");
        }

        if (chosen.Distinct().Count() != chosen.Count)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "departmentIds", "Choose each department once.");
        }

        return chosen.All(id => found.Any(c => c.Id == id && c.Domain == DomainCodes.Design))
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "departmentIds", "Choose departments of Design.");
    }

    /// <summary>Departments with hours logged on the project stay on it.</summary>
    public static RuleDenial? CheckRemoval(IReadOnlyList<string> removedWithHours)
        => removedWithHours.Count switch
        {
            0 => null,
            1 => RuleDenial.BusinessRule(ErrorCodes.DepartmentHasHours,
                $"{removedWithHours[0]} has hours logged on this project, so it can't be taken off it."),
            _ => RuleDenial.BusinessRule(ErrorCodes.DepartmentHasHours,
                $"{string.Join(", ", removedWithHours.SkipLast(1))} and {removedWithHours[^1]} have hours logged on this project, so they can't be taken off it."),
        };

    /// <summary>A share for each of the project's departments and only them, each 0 to 100%, adding up to exactly 100%.</summary>
    public static RuleDenial? CheckShares(IReadOnlyCollection<long> departments, IReadOnlyList<(long TeamId, decimal Share)> shares)
    {
        if (shares.Count != departments.Count || shares.Select(s => s.TeamId).Distinct().Count() != shares.Count
            || shares.Any(s => !departments.Contains(s.TeamId)))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "shares", "Give a share to each of the project's departments, and only to them.");
        }

        if (shares.Any(s => s.Share is < 0 or > 1 || decimal.Round(s.Share, 4) != s.Share))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "shares", "Each share is between 0% and 100%, to two decimal places.");
        }

        return shares.Sum(s => s.Share) == 1
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "shares", "The shares must add up to 100%.");
    }

    /// <summary>Whether a project worked on by <paramref name="departments"/> is a full file: every Design department, two or more.</summary>
    public static bool IsFullFile(IReadOnlyCollection<long> departments, IReadOnlyCollection<long> allDepartments)
        => departments.Count >= 2 && allDepartments.All(departments.Contains);

    /// <summary>
    /// Whether someone in <paramref name="department"/> logs hours on a project with <paramref name="departments"/>: a
    /// project with none takes anyone's, as before departments.
    /// </summary>
    public static bool MayLog(IReadOnlyCollection<long> departments, long? department)
        => departments.Count == 0 || department is { } d && departments.Contains(d);

    /// <summary>Someone with no department logs only on a project with none.</summary>
    public static RuleDenial NoDepartment()
        => RuleDenial.BusinessRule(ErrorCodes.DepartmentRequired,
            "You aren't in a department yet, so you can't log hours on a project with departments. Ask an admin to give you one.");
}
