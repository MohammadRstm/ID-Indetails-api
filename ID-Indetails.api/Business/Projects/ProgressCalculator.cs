using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// Progress is done ÷ total requirements, rounded to a whole percentage. A project with no
/// requirements is 0% while open and 100% once completed or archived.
/// </summary>
public static class ProgressCalculator
{
    public static byte Percent(int total, int done, ProjectStatus status)
    {
        if (total == 0)
        {
            return status is ProjectStatus.Completed or ProjectStatus.Archived ? (byte)100 : (byte)0;
        }

        return (byte)Math.Round(done * 100m / total, MidpointRounding.AwayFromZero);
    }
}
