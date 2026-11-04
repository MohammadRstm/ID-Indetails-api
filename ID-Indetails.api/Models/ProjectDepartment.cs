namespace ID_Indetails.api.Models;

/// <summary>
/// A Design department (a team) working on a project, with its share of the project's price. A project with two or
/// more has a budget for each; one with a single department works as before. Shares add up to 1.
/// </summary>
public class ProjectDepartment
{
    public long ProjectId { get; set; }

    public long TeamId { get; set; }

    /// <summary>Its part of the project's price, a fraction (0.25 is 25%).</summary>
    public decimal Share { get; set; }

    public DateTime AddedAt { get; set; }

    public long? AddedById { get; set; }

    public Project Project { get; set; } = null!;

    public Team Team { get; set; } = null!;
}
