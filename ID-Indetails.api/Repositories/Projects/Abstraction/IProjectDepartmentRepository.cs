using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>A department on a project, with its name and share.</summary>
public sealed record ProjectDepartmentRow(long ProjectId, long TeamId, string Name, decimal Share);

/// <summary>A change of a project's split, with who made it.</summary>
public sealed record ProjectDepartmentChangeRow(ProjectDepartmentChange Change, string ChangedByName);

/// <summary>A Design project's departments and the history of their shares.</summary>
public interface IProjectDepartmentRepository
{
    /// <summary>The departments of each project, by name.</summary>
    Task<IReadOnlyList<ProjectDepartmentRow>> ListAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>A project's departments, tracked, to change.</summary>
    Task<IReadOnlyList<ProjectDepartment>> ListForUpdateAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The Design departments there are, and those of <paramref name="ids"/> that exist, with their domain.</summary>
    Task<IReadOnlyList<ProjectDepartmentRules.Candidate>> CandidatesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Every Design department, to tell a full file.</summary>
    Task<IReadOnlyList<long>> AllDesignAsync(CancellationToken cancellationToken = default);

    /// <summary>The departments with hours logged on the project.</summary>
    Task<IReadOnlyList<long>> WithHoursAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>People's names, by id.</summary>
    Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>How many projects a team is a department of.</summary>
    Task<int> ProjectCountAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>A project's changes of split, newest first.</summary>
    Task<IReadOnlyList<ProjectDepartmentChangeRow>> ChangesAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(ProjectDepartment department);

    void Remove(ProjectDepartment department);

    void AddChange(ProjectDepartmentChange change);
}
