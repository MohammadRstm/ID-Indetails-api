using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Comments on work entries, readable by whoever can read the entry. Comments can't be edited or deleted.
/// The entry's author is notified of each comment someone else writes.
/// </summary>
public sealed class WorkEntryCommentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWorkEntryRepository entries,
    IWorkEntryCommentRepository comments,
    INotificationRepository notifications,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IWorkEntryCommentService
{
    public async Task<IReadOnlyList<WorkEntryCommentResponse>> ListAsync(long workEntryId, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var entry = await entries.GetViewAsync(workEntryId, cancellationToken);
        if (entry is null || !WorkEntryQueryService.CanRead(actor, entry))
        {
            throw new NotFoundException("Work entry not found.");
        }

        return (await comments.ListAsync(workEntryId, cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<WorkEntryCommentResponse> AddAsync(
        long workEntryId,
        AddWorkEntryCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var entry = await entries.GetViewAsync(workEntryId, cancellationToken);
        if (entry is null || !actor.CanSee(entry.ProjectDomain))
        {
            throw new NotFoundException("Work entry not found.");
        }

        if (request.SendToGroup)
        {
            throw new BusinessRuleException(ErrorCodes.GroupChatUnavailable, "Group chats aren't available yet, so the comment can't be posted there.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var comment = new WorkEntryComment
        {
            WorkEntryId = entry.Id,
            AuthorId = actor.UserId,
            Body = request.Body.Trim(),
            CreatedAt = clock.UtcNow,
        };
        comments.Add(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(new ActivityEvent
        {
            DomainCode = entry.ProjectDomain,
            Kind = ActivityEventKinds.WorkEntryCommented,
            OccurredAt = clock.UtcNow,
            ActorId = actor.UserId,
            ProjectId = entry.ProjectId,
            SubjectType = "work_entry",
            SubjectId = entry.Id,
            Payload = JsonSerializer.Serialize(new { commentId = comment.Id }),
        });

        if (entry.UserId != actor.UserId)
        {
            notifications.Add(new Notification
            {
                RecipientId = entry.UserId,
                Kind = NotificationKinds.EntryComment,
                ActorId = actor.UserId,
                ProjectId = entry.ProjectId,
                SubjectType = "work_entry",
                SubjectId = entry.Id,
                Payload = JsonSerializer.Serialize(new { authorName = currentUser.FullName, text = comment.Body, entryDescription = entry.Description }),
                CreatedAt = clock.UtcNow,
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await comments.ListAsync(workEntryId, cancellationToken)).Where(c => c.Id == comment.Id).Select(ToResponse).Single();
    }

    private static WorkEntryCommentResponse ToResponse(WorkEntryCommentView c)
        => new(c.Id, new WorkEntryCommentAuthorResponse(c.AuthorId, c.AuthorName, c.AuthorRole), c.Body, c.SentToGroup, c.CreatedAt);
}
