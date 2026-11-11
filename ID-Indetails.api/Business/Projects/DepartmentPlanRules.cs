using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// The hours planned for each department of a Design project (the owner's decision Q7): progress by hours is the hours
/// logged in the department ÷ the hours planned for it. A department has one plan at a time; a change replaces it, and
/// clearing it leaves the department without a plan, so it has no progress by hours.
/// </summary>
public static class DepartmentPlanRules
{
    public const decimal MaxHours = 100_000m;

    /// <summary>The most departments one request may plan; a project has far fewer.</summary>
    public const int MaxDepartments = 50;

    /// <summary>A department's planned hours as they are kept: whole minutes, to the nearest one.</summary>
    public static int MinutesOf(decimal hours) => (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The departments being planned against the project's: each one of its departments, and each once.
    /// </summary>
    /// <param name="teamIds">The departments in the request, in its order.</param>
    /// <param name="onProject">The project's departments.</param>
    public static RuleDenial? Check(IReadOnlyList<long> teamIds, IReadOnlyCollection<long> onProject)
    {
        for (var i = 0; i < teamIds.Count; i++)
        {
            if (!onProject.Contains(teamIds[i]))
            {
                return RuleDenial.Invalid(ErrorCodes.ValidationFailed, $"departments[{i}].teamId",
                    "That department doesn't work on this project: add it to the project first.");
            }
        }

        return teamIds.Distinct().Count() == teamIds.Count
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "departments", "Plan each department once.");
    }

    /// <summary>
    /// How far a department is by hours: the minutes logged in it ÷ the minutes planned, a fraction to four places
    /// (0.5 is half). It passes 1 once more hours are logged than planned. Null without a plan.
    /// </summary>
    public static decimal? Progress(int loggedMinutes, int? plannedMinutes)
        => plannedMinutes is { } planned && planned > 0
            ? Math.Round((decimal)loggedMinutes / planned, 4, MidpointRounding.AwayFromZero)
            : null;
}
