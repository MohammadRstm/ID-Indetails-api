namespace ID_Indetails.api.Models;

/// <summary>
/// The hours planned for a department of a Design project, kept in minutes: one at a time per department. A change
/// replaces it and clearing it removes it; each is recorded in the activity log with the value before.
/// </summary>
public class ProjectDepartmentPlan
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long TeamId { get; set; }

    public int PlannedMinutes { get; set; }

    public long SetById { get; set; }

    public DateTime SetAt { get; set; }
}
