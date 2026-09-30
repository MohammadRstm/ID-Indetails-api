using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Meetings.Implementation;

/// <summary>
/// Project meetings (see <see cref="MeetingRules"/>). Everyone who sees a project sees its meetings. Meetings of a
/// domain the caller can't see are not found. When someone who doesn't manage meetings sets one, the people who do
/// (other than them) are notified.
/// </summary>
public sealed class MeetingService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IMeetingRepository meetings,
    INotificationSender notifications,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IMeetingService
{
    public async Task<IReadOnlyList<MeetingResponse>> ListAsync(MeetingListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var rows = await meetings.ListAsync(
            new MeetingSearch(
                query.Domain is { } domain ? actor.VisibleDomains.Where(d => d == domain).ToList() : actor.VisibleDomains,
                query.ProjectId,
                query.Mine ? actor.UserId : null,
                query.From is { } from ? clock.CompanyDayStartUtc(from) : null,
                query.To is { } to ? clock.CompanyDayStartUtc(to.AddDays(1)) : null),
            cancellationToken);

        return rows.Select(MeetingMapper.ToResponse).ToList();
    }

    public async Task<IReadOnlyList<MeetingResponse>> ListForProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await VisibleProjectAsync(actor, projectId, cancellationToken);
        return (await meetings.ListAsync(new MeetingSearch(actor.VisibleDomains, projectId), cancellationToken))
            .Select(MeetingMapper.ToResponse)
            .ToList();
    }

    public async Task<MeetingResponse> CreateAsync(long projectId, SaveMeetingRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await VisibleProjectAsync(actor, projectId, cancellationToken);
        Throw(MeetingRules.CheckProject(project.Status));

        var meeting = new ProjectMeeting { ProjectId = project.Id, CreatedAt = clock.UtcNow, CreatedById = actor.UserId };
        Apply(meeting, request);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        meetings.Add(meeting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        activity.Add(MeetingEvent(meeting, project.DomainCode, ActivityEventKinds.MeetingCreated, actor.UserId));
        if (!actor.Has(PermissionCodes.MeetingManage))
        {
            await NotifyManagersAsync(meeting, project, actor.UserId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(meeting.Id, cancellationToken);
    }

    public async Task<MeetingResponse> UpdateAsync(long id, SaveMeetingRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var meeting = await VisibleMeetingAsync(actor, id, cancellationToken);
        Throw(CheckChange(actor, meeting));
        Throw(MeetingRules.CheckProject(meeting.Project.Status));

        Apply(meeting, request);
        meeting.UpdatedAt = clock.UtcNow;
        meeting.UpdatedById = actor.UserId;
        activity.Add(MeetingEvent(meeting, meeting.Project.DomainCode, ActivityEventKinds.MeetingUpdated, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(meeting.Id, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var meeting = await VisibleMeetingAsync(actor, id, cancellationToken);
        Throw(CheckChange(actor, meeting));

        meeting.DeletedAt = clock.UtcNow;
        activity.Add(MeetingEvent(meeting, meeting.Project.DomainCode, ActivityEventKinds.MeetingDeleted, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken) => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<Project> VisibleProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException("Project not found.");
    }

    private async Task<ProjectMeeting> VisibleMeetingAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetForUpdateAsync(id, cancellationToken);
        return meeting is not null && actor.CanSee(meeting.Project.DomainCode) ? meeting : throw new NotFoundException("Meeting not found.");
    }

    private static RuleDenial? CheckChange(UserAccess actor, ProjectMeeting meeting)
        => MeetingRules.CheckChange(
            actor.Has(PermissionCodes.MeetingManage), actor.Has(PermissionCodes.MeetingCreate), meeting.CreatedById, actor.UserId);

    private async Task NotifyManagersAsync(ProjectMeeting meeting, Project project, long actorId, CancellationToken cancellationToken)
        => await notifications.SendAsync(
            new NotificationDraft(
                NotificationKinds.MeetingScheduled,
                actorId,
                project.DomainCode,
                project.Id,
                "meeting",
                meeting.Id,
                new
                {
                    authorName = currentUser.FullName,
                    projectTitle = project.Title,
                    topic = meeting.Topic,
                    startsAt = meeting.StartsAt,
                    endsAt = meeting.EndsAt,
                }),
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.MeetingManage, cancellationToken),
            cancellationToken);

    private void Apply(ProjectMeeting meeting, SaveMeetingRequest request)
    {
        var startsAt = WorkEntryRules.AsUtc(request.StartsAt);
        var endsAt = WorkEntryRules.AsUtc(request.EndsAt);
        Throw(MeetingRules.CheckTimes(startsAt, endsAt, clock.ToCompanyDate(startsAt), clock.ToCompanyDate(endsAt.AddMinutes(-1))));

        meeting.StartsAt = startsAt;
        meeting.EndsAt = endsAt;
        meeting.Topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        meeting.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    }

    private async Task<MeetingResponse> DescribeAsync(long id, CancellationToken cancellationToken)
        => MeetingMapper.ToResponse(await meetings.GetViewAsync(id, cancellationToken) ?? throw new NotFoundException("Meeting not found."));

    private ActivityEvent MeetingEvent(ProjectMeeting meeting, string domainCode, string kind, long actorId) => new()
    {
        DomainCode = domainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = meeting.ProjectId,
        SubjectType = "meeting",
        SubjectId = meeting.Id,
        Payload = JsonSerializer.Serialize(new { topic = meeting.Topic, startsAt = meeting.StartsAt, endsAt = meeting.EndsAt }),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
