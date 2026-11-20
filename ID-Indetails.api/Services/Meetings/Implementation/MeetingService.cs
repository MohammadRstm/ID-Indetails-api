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
using ID_Indetails.api.Services.TimeOff.Abstraction;

namespace ID_Indetails.api.Services.Meetings.Implementation;

/// <summary>
/// Project meetings and general meetings (see <see cref="MeetingRules"/>). Everyone who sees a project sees its
/// meetings; a general meeting is for its attendees. Meetings the caller can't see are not found. When someone who
/// doesn't manage meetings sets a project meeting, the people who do (other than them and its invitees) are notified.
/// A project meeting's invitees and a general meeting's attendees are told when they are invited, when it moves, and
/// when it is cancelled for them. Nobody is put in a meeting, or has one moved onto, a day of their time off; the
/// meetings they were already in stay.
/// </summary>
public sealed class MeetingService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IMeetingRepository meetings,
    INotificationSender notifications,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    ITimeOffWatch timeOff,
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

    public async Task<IReadOnlyList<MeetingInviteeResponse>> InviteesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await VisibleProjectAsync(actor, projectId, cancellationToken);
        var ids = (await ProjectPeopleAsync(project, cancellationToken)).Where(id => id != actor.UserId).ToList();
        return (await meetings.PeopleAsync(ids, cancellationToken))
            .Select(p => new MeetingInviteeResponse(p.Id, p.Name, p.PhotoFileId, p.Team))
            .ToList();
    }

    public async Task<MeetingResponse> CreateAsync(long projectId, SaveMeetingRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await VisibleProjectAsync(actor, projectId, cancellationToken);
        Throw(MeetingRules.CheckNewMeeting(project.Status));
        var inviteeIds = await ProjectInviteesAsync(actor, project, request.AttendeeIds ?? [], cancellationToken);

        var meeting = new ProjectMeeting { ProjectId = project.Id, CreatedAt = clock.UtcNow, CreatedById = actor.UserId };
        Apply(meeting, request);
        await CheckOffAsync(meeting, inviteeIds, cancellationToken);
        foreach (var userId in inviteeIds)
        {
            meeting.Attendees.Add(new MeetingAttendee { UserId = userId });
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        meetings.Add(meeting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        activity.Add(MeetingEvent(meeting, project.DomainCode, ActivityEventKinds.MeetingCreated, actor.UserId));
        if (!actor.Has(PermissionCodes.MeetingManage))
        {
            await NotifyManagersAsync(meeting, project, actor.UserId, inviteeIds, cancellationToken);
        }

        await TellAttendeesAsync(NotificationKinds.MeetingInvited, meeting, project, inviteeIds, actor.UserId, cancellationToken);
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
        await CheckOffAsync(meeting, attendeeIds, cancellationToken);
        foreach (var userId in attendeeIds)
        {
            meeting.Attendees.Add(new MeetingAttendee { UserId = userId });
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        meetings.Add(meeting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await TellAttendeesAsync(NotificationKinds.MeetingInvited, meeting, null, attendeeIds, actor.UserId, cancellationToken);
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
        }

        var (startsAt, endsAt) = (meeting.StartsAt, meeting.EndsAt);
        Apply(meeting, request);
        meeting.UpdatedAt = clock.UtcNow;
        meeting.UpdatedById = actor.UserId;
        var moved = (startsAt, endsAt) != (meeting.StartsAt, meeting.EndsAt);

        var before = meeting.Attendees.Select(a => a.UserId).ToHashSet();
        var after = request.AttendeeIds is not { } chosen
            ? before
            : (meeting.Project is { } owner
                ? await ProjectInviteesAsync(actor, owner, chosen, cancellationToken)
                : await AttendeesAsync(actor, meeting.DomainCode, chosen, cancellationToken)).ToHashSet();
        await CheckOffAsync(meeting, moved ? after.ToList() : after.Except(before).ToList(), cancellationToken);
        foreach (var gone in meeting.Attendees.Where(a => !after.Contains(a.UserId)).ToList())
        {
            meeting.Attendees.Remove(gone);
        }

        foreach (var userId in after.Where(u => !before.Contains(u)))
        {
            meeting.Attendees.Add(new MeetingAttendee { UserId = userId });
        }

        if (meeting.Project is { } onProject)
        {
            activity.Add(MeetingEvent(meeting, onProject.DomainCode, ActivityEventKinds.MeetingUpdated, actor.UserId));
        }

        await TellAttendeesAsync(NotificationKinds.MeetingInvited, meeting, meeting.Project, after.Except(before).ToList(), actor.UserId, cancellationToken);
        await TellAttendeesAsync(NotificationKinds.MeetingCancelled, meeting, meeting.Project, before.Except(after).ToList(), actor.UserId, cancellationToken);
        if (moved)
        {
            await TellAttendeesAsync(NotificationKinds.MeetingMoved, meeting, meeting.Project, before.Intersect(after).ToList(), actor.UserId, cancellationToken);
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

        await TellAttendeesAsync(
            NotificationKinds.MeetingCancelled, meeting, meeting.Project, meeting.Attendees.Select(a => a.UserId).ToList(), actor.UserId, cancellationToken);

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

    /// <summary>A project meeting's invitees: optional, each someone who sees the project (see <see cref="MeetingRules.CheckInvitees"/>).</summary>
    private async Task<IReadOnlyList<long>> ProjectInviteesAsync(UserAccess actor, Project project, IReadOnlyList<long> chosen, CancellationToken cancellationToken)
    {
        var ids = chosen.Distinct().Where(id => id != actor.UserId).ToList();
        if (ids.Count > 0)
        {
            Throw(MeetingRules.CheckInvitees(ids, (await ProjectPeopleAsync(project, cancellationToken)).ToHashSet()));
        }

        return ids;
    }

    /// <summary>Everyone who sees the project: the active people who hold <c>project:view</c> and see its domain, super admins included.</summary>
    private Task<IReadOnlyList<long>> ProjectPeopleAsync(Project project, CancellationToken cancellationToken)
        => notifications.HoldersAsync(project.DomainCode, PermissionCodes.ProjectView, cancellationToken);

    /// <summary>No one goes into a meeting on a day of their time off.</summary>
    private Task CheckOffAsync(ProjectMeeting meeting, IReadOnlyList<long> attendeeIds, CancellationToken cancellationToken)
        => timeOff.CheckMeetingAsync(attendeeIds, clock.ToCompanyDate(meeting.StartsAt), clock.ToCompanyDate(meeting.EndsAt.AddMinutes(-1)), cancellationToken);

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

    /// <summary>Its invitees among them get their invitation instead.</summary>
    private async Task NotifyManagersAsync(
        ProjectMeeting meeting, Project project, long actorId, IReadOnlyList<long> inviteeIds, CancellationToken cancellationToken)
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
            (await notifications.HoldersAsync(project.DomainCode, PermissionCodes.MeetingManage, cancellationToken)).Except(inviteeIds),
            cancellationToken);

    /// <summary>A project meeting's invitees or a general meeting's attendees; a project meeting's carry its project.</summary>
    private async Task TellAttendeesAsync(
        string kind, ProjectMeeting meeting, Project? project, IReadOnlyList<long> attendeeIds, long actorId, CancellationToken cancellationToken)
    {
        if (attendeeIds.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(
            new NotificationDraft(
                kind,
                actorId,
                project?.DomainCode ?? meeting.DomainCode,
                project?.Id,
                "meeting",
                meeting.Id,
                new
                {
                    authorName = currentUser.FullName,
                    projectTitle = project?.Title,
                    topic = meeting.Topic,
                    startsAt = meeting.StartsAt,
                    endsAt = meeting.EndsAt,
                }),
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
