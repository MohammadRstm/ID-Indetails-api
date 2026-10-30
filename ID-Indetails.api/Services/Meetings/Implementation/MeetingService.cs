using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
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
/// Project meetings and general meetings (see <see cref="MeetingRules"/>). Everyone who sees a project sees its
/// meetings; a general meeting is for its attendees. Meetings the caller can't see are not found. When someone who
/// doesn't manage meetings sets a project meeting, the people who do (other than them) are notified; a general
/// meeting's attendees are told when they are invited, when it moves, and when it is cancelled for them.
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
                MeetingRules.ViewerOf(actor),
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
        return (await meetings.ListAsync(new MeetingSearch(actor.VisibleDomains, MeetingRules.ViewerOf(actor), projectId), cancellationToken))
            .Select(MeetingMapper.ToResponse)
            .ToList();
    }

    public async Task<MeetingResponse> CreateAsync(long projectId, SaveMeetingRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await VisibleProjectAsync(actor, projectId, cancellationToken);
        Throw(MeetingRules.CheckNewMeeting(project.Status));
        Throw(NoAttendees(request));

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

    public async Task<MeetingResponse> CreateGeneralAsync(SaveGeneralMeetingRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var domain = DomainOf(actor, request.Domain);
        var attendeeIds = await AttendeesAsync(actor, domain, request.AttendeeIds, cancellationToken);

        var meeting = new ProjectMeeting { DomainCode = domain, CreatedAt = clock.UtcNow, CreatedById = actor.UserId };
        Apply(meeting, request.StartsAt, request.EndsAt, request.Topic, request.Description);
        foreach (var userId in attendeeIds)
        {
            meeting.Attendees.Add(new MeetingAttendee { UserId = userId });
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        meetings.Add(meeting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await TellAttendeesAsync(NotificationKinds.MeetingInvited, meeting, attendeeIds, actor.UserId, cancellationToken);
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
        if (meeting.Project is { } project)
        {
            Throw(MeetingRules.CheckProject(project.Status));
            Throw(NoAttendees(request));
        }

        var (startsAt, endsAt) = (meeting.StartsAt, meeting.EndsAt);
        Apply(meeting, request);
        meeting.UpdatedAt = clock.UtcNow;
        meeting.UpdatedById = actor.UserId;

        if (meeting.Project is { } owner)
        {
            activity.Add(MeetingEvent(meeting, owner.DomainCode, ActivityEventKinds.MeetingUpdated, actor.UserId));
        }
        else
        {
            var before = meeting.Attendees.Select(a => a.UserId).ToHashSet();
            var after = request.AttendeeIds is { } chosen
                ? (await AttendeesAsync(actor, meeting.DomainCode, chosen, cancellationToken)).ToHashSet()
                : before;
            foreach (var gone in meeting.Attendees.Where(a => !after.Contains(a.UserId)).ToList())
            {
                meeting.Attendees.Remove(gone);
            }

            foreach (var userId in after.Where(u => !before.Contains(u)))
            {
                meeting.Attendees.Add(new MeetingAttendee { UserId = userId });
            }

            await TellAttendeesAsync(NotificationKinds.MeetingInvited, meeting, after.Except(before).ToList(), actor.UserId, cancellationToken);
            await TellAttendeesAsync(NotificationKinds.MeetingCancelled, meeting, before.Except(after).ToList(), actor.UserId, cancellationToken);
            if ((startsAt, endsAt) != (meeting.StartsAt, meeting.EndsAt))
            {
                await TellAttendeesAsync(NotificationKinds.MeetingMoved, meeting, before.Intersect(after).ToList(), actor.UserId, cancellationToken);
            }
        }

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
        if (meeting.Project is { } project)
        {
            Throw(MeetingRules.CheckProject(project.Status));
        }

        meeting.DeletedAt = clock.UtcNow;
        if (meeting.Project is { } owner)
        {
            activity.Add(MeetingEvent(meeting, owner.DomainCode, ActivityEventKinds.MeetingDeleted, actor.UserId));
        }
        else
        {
            await TellAttendeesAsync(NotificationKinds.MeetingCancelled, meeting, meeting.Attendees.Select(a => a.UserId).ToList(), actor.UserId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken) => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>
    /// A general meeting's domain: the one asked for, which must be the caller's to see; otherwise the whole company for
    /// someone who sees it, or the caller's own business.
    /// </summary>
    private static string? DomainOf(UserAccess actor, string? asked)
    {
        if (asked is not null)
        {
            return actor.CanSee(asked) ? asked : throw new NotFoundException("Domain not found.");
        }

        return actor.Has(PermissionCodes.CompanyView)
            ? null
            : actor.VisibleBusinesses.FirstOrDefault()
                ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "domain", "Choose the meeting's domain.");
    }

    /// <summary>The people chosen, but whoever sets it, checked against those they may invite (see <see cref="MeetingRules.CheckAttendees"/>).</summary>
    private async Task<IReadOnlyList<long>> AttendeesAsync(UserAccess actor, string? domain, IReadOnlyList<long> chosen, CancellationToken cancellationToken)
    {
        var ids = chosen.Distinct().Where(id => id != actor.UserId).ToList();
        // someone the setter sees, and who belongs where the meeting does (super admins belong everywhere)
        var invitable = (await meetings.ActivePeopleAsync(ids, cancellationToken))
            .Where(p => EmployeeRules.CanSee(actor, p.DomainCode) && (domain is null || p.DomainCode is null || p.DomainCode == domain))
            .Select(p => p.Id)
            .ToHashSet();
        Throw(MeetingRules.CheckAttendees(ids, invitable));
        return ids;
    }

    private static RuleDenial? NoAttendees(SaveMeetingRequest request)
        => request.AttendeeIds is null
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "attendeeIds", "A project meeting is for everyone on the project: it has no attendees.");

    private async Task<Project> VisibleProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException("Project not found.");
    }

    private async Task<ProjectMeeting> VisibleMeetingAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetForUpdateAsync(id, cancellationToken);
        var visible = meeting switch
        {
            null => false,
            { Project: { } project } => actor.CanSee(project.DomainCode),
            _ => MeetingRules.CanSeeGeneral(
                new MeetingRules.GeneralMeeting(meeting.DomainCode, meeting.CreatedById, meeting.Attendees.Select(a => a.UserId).ToList()),
                MeetingRules.ViewerOf(actor)),
        };
        return visible ? meeting! : throw new NotFoundException("Meeting not found.");
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

    private async Task TellAttendeesAsync(string kind, ProjectMeeting meeting, IReadOnlyList<long> attendeeIds, long actorId, CancellationToken cancellationToken)
    {
        if (attendeeIds.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(
            new NotificationDraft(
                kind,
                actorId,
                meeting.DomainCode,
                null,
                "meeting",
                meeting.Id,
                new { authorName = currentUser.FullName, topic = meeting.Topic, startsAt = meeting.StartsAt, endsAt = meeting.EndsAt }),
            attendeeIds,
            cancellationToken);
    }

    private void Apply(ProjectMeeting meeting, SaveMeetingRequest request)
        => Apply(meeting, request.StartsAt, request.EndsAt, request.Topic, request.Description);

    private void Apply(ProjectMeeting meeting, DateTime start, DateTime end, string? topic, string? description)
    {
        var startsAt = WorkEntryRules.AsUtc(start);
        var endsAt = WorkEntryRules.AsUtc(end);
        Throw(MeetingRules.CheckTimes(startsAt, endsAt, clock.ToCompanyDate(startsAt), clock.ToCompanyDate(endsAt.AddMinutes(-1))));

        meeting.StartsAt = startsAt;
        meeting.EndsAt = endsAt;
        meeting.Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
        meeting.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
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
