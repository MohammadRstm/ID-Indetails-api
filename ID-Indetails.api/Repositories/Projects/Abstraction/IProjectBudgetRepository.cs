using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>A line of a budget version, with its department's name as it is now (null for a category line).</summary>
public sealed record ProjectBudgetLineRow(long? TeamId, string? TeamName, string? Category, decimal AmountUsd);

/// <summary>A version of a project's budget, with who set it and its lines in the order they were given.</summary>
public sealed record ProjectBudgetRow(
    int Version,
    DateOnly EffectiveFrom,
    string Reason,
    string SetByName,
    DateTime SetAt,
    IReadOnlyList<ProjectBudgetLineRow> Lines);

/// <summary>A project's cost budget: its versions, added and never edited.</summary>
public interface IProjectBudgetRepository
{
    /// <summary>Every version of the project's budget, newest first.</summary>
    Task<IReadOnlyList<ProjectBudgetRow>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Whether a line of any version of any project's budget is the team's.</summary>
    Task<bool> HasTeamAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new version with its lines; saved by the unit of work.</summary>
    void Add(ProjectBudget budget);
}
