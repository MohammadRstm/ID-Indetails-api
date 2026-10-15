using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>The team's comments on a Design project's files. Clients never see them.</summary>
public interface IProjectFileCommentService
{
    /// <summary>The file's comments, oldest first.</summary>
    Task<IReadOnlyList<ProjectFileCommentResponse>> ListAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    Task<ProjectFileCommentResponse> AddAsync(long projectId, long itemId, ProjectFileCommentRequest request, CancellationToken cancellationToken = default);

    Task<ProjectFileCommentResponse> EditAsync(long projectId, long commentId, ProjectFileCommentRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long projectId, long commentId, CancellationToken cancellationToken = default);

    Task<ProjectFileCommentResponse> SetFixedAsync(long projectId, long commentId, ProjectFileFixedRequest request, CancellationToken cancellationToken = default);
}
