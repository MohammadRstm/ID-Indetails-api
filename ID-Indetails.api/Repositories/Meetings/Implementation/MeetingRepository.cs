using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Meetings.Implementation;

public sealed class MeetingRepository(IdIndetailsDbContext db) : IMeetingRepository
{
    public async Task<IReadOnlyList<MeetingView>> ListAsync(MeetingSearch search, CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.ProjectMeetings.Where(m => m.DeletedAt == null && domains.Contains(m.Project.DomainCode));
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
        => db.ProjectMeetings.Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, cancellationToken);

    public void Add(ProjectMeeting meeting) => db.ProjectMeetings.Add(meeting);

    private async Task<IReadOnlyList<MeetingView>> ViewsAsync(IQueryable<ProjectMeeting> meetings, CancellationToken cancellationToken)
    {
        var rows = await meetings
            .AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.ProjectId,
                m.Project.Title,
                m.Project.DomainCode,
                m.StartsAt,
                m.EndsAt,
                m.Topic,
                m.Description,
                m.CreatedById,
                m.CreatedAt,
                m.UpdatedAt,
                m.Project.Icon,
                m.Project.Tint,
            })
            .ToListAsync(cancellationToken);

        var creatorIds = rows.Where(r => r.CreatedById is not null).Select(r => r.CreatedById!.Value).Distinct().ToList();
        var creators = await db.Users
            .AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email), cancellationToken);

        return rows
            .Select(r => new MeetingView(
                r.Id, r.ProjectId, r.Title, r.DomainCode, r.StartsAt, r.EndsAt, r.Topic, r.Description, r.CreatedById,
                r.CreatedById is { } c ? creators.GetValueOrDefault(c) : null, r.CreatedAt, r.UpdatedAt, r.Icon, r.Tint))
            .ToList();
    }
}
