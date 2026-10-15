using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The team's comments on a project's files (<see cref="ProjectFileRules"/>): everyone who can open the project reads
/// them; managers start a file's comments and anyone replies. A new comment tells the file's uploader and the earlier
/// commenters, rolling per file, naming who and which file but never the text.
/// </summary>
public sealed class ProjectFileCommentService(
    ICurrentUser currentUser,
    IPortalContentViews views,
    IPortalContentRepository content,
    IProjectFileCommentRepository comments,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectFileCommentService
{
    private const string ItemNotFound = "File not found.";
    private const string CommentNotFound = "Comment not found.";

    public async Task<IReadOnlyList<ProjectFileCommentResponse>> ListAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
    {
        var access = await views.FilesAccessAsync(projectId, cancellationToken);
        var item = await ItemAsync(projectId, itemId, cancellationToken);
        return (await comments.ListAsync(itemId, cancellationToken)).Select(c => Describe(access, c, item.File.UploadedById)).ToList();
    }

    public async Task<ProjectFileCommentResponse> AddAsync(
        long projectId, long itemId, ProjectFileCommentRequest request, CancellationToken cancellationToken = default)
    {
        var access = await EditableAccessAsync(projectId, cancellationToken);
        var item = await ItemAsync(projectId, itemId, cancellationToken);
        var earlier = await comments.AuthorsAsync(itemId, cancellationToken);
        if (!ProjectFileRules.CanComment(access, earlier.Count > 0))
        {
            throw new ForbiddenException(
                ErrorCodes.Forbidden, "Only people who manage project files can start the comments on a file. Anyone can reply once there are some.");
        }

        var comment = new ProjectFileComment
        {
            ProjectId = projectId,
            ItemId = itemId,
            AuthorId = currentUser.Id,
            Body = ProjectFileRules.Clean(request.Body),
            CreatedAt = clock.UtcNow,
        };
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            comments.Add(comment);
            await notifications.SendDigestAsync(
                new NotificationDraft(
                    NotificationKinds.ProjectFileComment, currentUser.Id, access.Project.DomainCode, projectId, "item", itemId,
                    new { projectTitle = access.Project.Title, fileTitle = item.Title, authorName = currentUser.FullName }),
                $"file_comments:{itemId}",
                ProjectFileRules.CommentRecipients(item.File.UploadedById, earlier, currentUser.Id),
                restartOnceRead: true,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await CommentAsync(access, comment.Id, item.File.UploadedById, cancellationToken);
    }

    public async Task<ProjectFileCommentResponse> EditAsync(
        long projectId, long commentId, ProjectFileCommentRequest request, CancellationToken cancellationToken = default)
    {
        var access = await EditableAccessAsync(projectId, cancellationToken);
        var comment = await OwnCommentAsync(projectId, commentId, cancellationToken);
        comment.Body = ProjectFileRules.Clean(request.Body);
        comment.EditedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var item = await ItemAsync(projectId, comment.ItemId, cancellationToken);
        return await CommentAsync(access, commentId, item.File.UploadedById, cancellationToken);
    }

    public async Task DeleteAsync(long projectId, long commentId, CancellationToken cancellationToken = default)
    {
        await EditableAccessAsync(projectId, cancellationToken);
        comments.Remove(await OwnCommentAsync(projectId, commentId, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectFileCommentResponse> SetFixedAsync(
        long projectId, long commentId, ProjectFileFixedRequest request, CancellationToken cancellationToken = default)
    {
        var access = await EditableAccessAsync(projectId, cancellationToken);
        var comment = await comments.GetAsync(projectId, commentId, cancellationToken) ?? throw new NotFoundException(CommentNotFound);
        var item = await ItemAsync(projectId, comment.ItemId, cancellationToken);
        if (!ProjectFileRules.CanEdit(access, item.File.UploadedById))
        {
            throw new ForbiddenException(
                ErrorCodes.Forbidden, "Only the person who uploaded this file, or someone who manages project files, can mark its comments fixed.");
        }

        if (request.Fixed != (comment.FixedAt is not null))
        {
            comment.FixedAt = request.Fixed ? clock.UtcNow : null;
            comment.FixedById = request.Fixed ? currentUser.Id : null;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await CommentAsync(access, commentId, item.File.UploadedById, cancellationToken);
    }

    /// <summary>The project's files as the caller sees them, while they can change: an archived project's are read-only.</summary>
    private async Task<ProjectFilesAccess> EditableAccessAsync(long projectId, CancellationToken cancellationToken)
    {
        var access = await views.FilesAccessAsync(projectId, cancellationToken);
        if (ProjectFileRules.CheckEditable(access.Project.Status) is { } denial)
        {
            throw denial.ToException();
        }

        return access;
    }

    private async Task<PortalItemView> ItemAsync(long projectId, long itemId, CancellationToken cancellationToken)
        => await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);

    /// <summary>The caller's own comment in this project; someone else's can't be changed.</summary>
    private async Task<ProjectFileComment> OwnCommentAsync(long projectId, long commentId, CancellationToken cancellationToken)
    {
        var comment = await comments.GetAsync(projectId, commentId, cancellationToken) ?? throw new NotFoundException(CommentNotFound);
        return comment.AuthorId == currentUser.Id
            ? comment
            : throw new ForbiddenException(ErrorCodes.Forbidden, "Only its author can change a comment.");
    }

    private async Task<ProjectFileCommentResponse> CommentAsync(
        ProjectFilesAccess access, long commentId, long uploaderId, CancellationToken cancellationToken)
        => Describe(access, await comments.ViewAsync(commentId, cancellationToken) ?? throw new NotFoundException(CommentNotFound), uploaderId);

    private static ProjectFileCommentResponse Describe(ProjectFilesAccess access, ProjectFileCommentView comment, long uploaderId)
    {
        var editable = ProjectFileRules.CheckEditable(access.Project.Status) is null;
        var mine = comment.AuthorId == access.UserId;
        return new ProjectFileCommentResponse
        {
            Id = comment.Id,
            ItemId = comment.ItemId,
            Author = new PersonRefResponse(comment.AuthorId, comment.AuthorName),
            Body = comment.Body,
            CreatedAt = comment.CreatedAt,
            EditedAt = comment.EditedAt,
            FixedAt = comment.FixedAt,
            FixedBy = comment.FixedById is { } fixerId && comment.FixedByName is { } fixerName ? new PersonRefResponse(fixerId, fixerName) : null,
            CanEdit = editable && mine,
            CanDelete = editable && mine,
            CanFix = editable && ProjectFileRules.CanEdit(access, uploaderId),
        };
    }
}
