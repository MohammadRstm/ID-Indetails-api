using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>A Design project's departments, their shares of the price and each one's budget and labour.</summary>
public interface IProjectDepartmentService
{
    /// <summary>The departments; their shares and money only with <c>financial:view</c>.</summary>
    Task<ProjectDepartmentsResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Every change of the split, newest first. Needs <c>financial:view</c>.</summary>
    Task<IReadOnlyList<ProjectDepartmentChangeResponse>> HistoryAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the departments (<c>project:edit</c>): the shares go back to equal, a department with hours logged on the
    /// project stays, and the hours waiting for an added department go through.
    /// </summary>
    Task<ProjectDepartmentsResponse> SetAsync(long projectId, SetProjectDepartmentsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets each department's share, adding up to 100%, with why (<c>financial:write</c>).</summary>
    Task<ProjectDepartmentsResponse> SetSharesAsync(long projectId, SetDepartmentSharesRequest request, CancellationToken cancellationToken = default);
}
