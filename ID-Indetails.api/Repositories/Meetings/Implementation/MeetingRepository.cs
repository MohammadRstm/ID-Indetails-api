using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Meetings.Implementation;

public sealed class MeetingRepository(IdIndetailsDbContext db) : IMeetingRepository
{
    public async Task<IReadOnlyList<MeetingView>> ListAsync(MeetingSearch search, CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var viewerId = search.Viewer.UserId;
        var manages = search.Viewer.ManagesMeetings;
        var seesCompany = search.Viewer.SeesCompany;
        var query = db.ProjectMeetings.Where(m => m.DeletedAt == null && (
            (m.ProjectId != null && domains.Contains(m.Project!.DomainCode))
            || (m.ProjectId == null
                && (m.DomainCode == null || domains.Contains(m.DomainCode))
                && (m.CreatedById == viewerId
                    || m.Attendees.Any(a => a.UserId == viewerId)
                    || (manages && (m.DomainCode != null || seesCompany))))));
        if (search.ProjectId is { } projectId)
        {
            query = query.Where(m => m.ProjectId == projectId);
        }

        if (search.CreatedById is { } createdById)
        {
            query = query.Where(m => m.CreatedById == createdById);
        }

        if (search.From is { } from)
        {
            query = query.Where(m => m.EndsAt > from);
        }

        if (search.To is { } to)
        {
            query = query.Where(m => m.StartsAt < to);
        }

        return await ViewsAsync(query.OrderBy(m => m.StartsAt).ThenBy(m => m.Id), cancellationToken);
    }

    public async Task<MeetingView?> GetViewAsync(long id, CancellationToken cancellationToken = default)
        => (await ViewsAsync(db.ProjectMeetings.Where(m => m.Id == id && m.DeletedAt == null), cancellationToken)).SingleOrDefault();

    public Task<ProjectMeeting?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.ProjectMeetings
            .Include(m => m.Project)
            .Include(m => m.Attendees)
            .SingleOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<MeetingInvitee>> ActivePeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active)
            .Select(u => new MeetingInvitee(u.Id, u.DomainCode))
            .ToListAsync(cancellationToken);

    public void Add(ProjectMeeting meeting) => db.ProjectMeetings.Add(meeting);

    private async Task<IReadOnlyList<MeetingView>> ViewsAsync(IQueryable<ProjectMeeting> meetings, CancellationToken cancellationToken)
    {
        var rows = await meetings
            .AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.ProjectId,
                Title = m.Project == null ? null : m.Project.Title,
                Domain = m.Project == null ? m.DomainCode : m.Project.DomainCode,
                m.StartsAt,
                m.EndsAt,
                m.Topic,
                m.Description,
                m.CreatedById,
                m.CreatedAt,
                m.UpdatedAt,
                Icon = m.Project == null ? null : m.Project.Icon,
                Tint = m.Project == null ? null : m.Project.Tint,
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var attendees = (await db.MeetingAttendees
                .AsNoTracking()
                .Where(a => ids.Contains(a.MeetingId))
                .Select(a => new { a.MeetingId, a.UserId, a.User.FirstName, a.User.LastName, a.User.Email })
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.MeetingId, a => new MeetingPerson(a.UserId, PersonName.Of(a.FirstName, a.LastName, a.Email)));

        var creatorIds = rows.Where(r => r.CreatedById is not null).Select(r => r.CreatedById!.Value).Distinct().ToList();
        var creators = await db.Users
            .AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email), cancellationToken);

        return rows
            .Select(r => new MeetingView(
                r.Id, r.ProjectId, r.Title, r.Domain, r.StartsAt, r.EndsAt, r.Topic, r.Description, r.CreatedById,
                r.CreatedById is { } c ? creators.GetValueOrDefault(c) : null, r.CreatedAt, r.UpdatedAt, r.Icon, r.Tint,
                attendees[r.Id].OrderBy(a => a.Name).ToList()))
            .ToList();
    }
}
