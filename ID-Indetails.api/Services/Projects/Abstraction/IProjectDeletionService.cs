using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>Deleting an archived project created by mistake, with everything on it (project:delete).</summary>
public interface IProjectDeletionService
{
    /// <summary>What deleting the project would remove, or why it can't be deleted.</summary>
    Task<ProjectDeletionResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Deletes it, once <paramref name="typedTitle"/> is its name exactly and nothing keeps it.</summary>
    Task DeleteAsync(long projectId, string? typedTitle, CancellationToken cancellationToken = default);
}
