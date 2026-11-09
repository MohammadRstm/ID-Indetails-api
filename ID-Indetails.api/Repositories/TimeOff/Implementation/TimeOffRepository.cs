using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.TimeOff.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.TimeOff.Implementation;

public sealed class TimeOffRepository(IdIndetailsDbContext db) : ITimeOffRepository
{
    private static readonly TimeOffStatus[] Granted = [TimeOffStatus.Approved, TimeOffStatus.Rejected, TimeOffStatus.Ended];

    public void Add(TimeOffRequest request) => db.TimeOffRequests.Add(request);

    public Task<TimeOffRequest?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.TimeOffRequests
            .FromSql($"SELECT * FROM `time_off_requests` WHERE `id` = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<TimeOffRow?> GetRowAsync(long id, CancellationToken cancellationToken = default)
        => (await RowsAsync(db.TimeOffRequests.Where(r => r.Id == id), cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<TimeOffRow>> OfUserAsync(long userId, CancellationToken cancellationToken = default)
        => RowsAsync(db.TimeOffRequests.Where(r => r.UserId == userId).OrderByDescending(r => r.FirstDay).ThenByDescending(r => r.Id), cancellationToken);

    public async Task<(IReadOnlyList<TimeOffRow> Items, int Total)> ListAsync(
        TimeOffView view, DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var requests = view switch
        {
            TimeOffView.Pending => db.TimeOffRequests.Where(r => r.Status == TimeOffStatus.Pending)
                .OrderBy(r => r.FirstDay).ThenBy(r => r.Id),
            TimeOffView.Current => db.TimeOffRequests.Where(r => r.Status == TimeOffStatus.Approved && r.LastDay >= today)
                .OrderBy(r => r.FirstDay).ThenBy(r => r.Id),
            _ => db.TimeOffRequests.Where(r => r.Status != TimeOffStatus.Pending && !(r.Status == TimeOffStatus.Approved && r.LastDay >= today))
                .OrderByDescending(r => r.FirstDay).ThenByDescending(r => r.Id),
        };

        var total = await requests.CountAsync(cancellationToken);
        var items = await RowsAsync(requests.Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return (items, total);
    }

    public Task<bool> HeldOnAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => db.TimeOffRequests.AnyAsync(
            r => r.UserId == userId
                && (r.Status == TimeOffStatus.Pending || r.Status == TimeOffStatus.Approved)
                && r.FirstDay <= to && r.LastDay >= from,
            cancellationToken);

    public async Task<IReadOnlyList<(long UserId, TimeOffRules.Period Period)>> GrantedAsync(
        IReadOnlyCollection<long>? userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var requests = db.TimeOffRequests.AsNoTracking()
            .Where(r => Granted.Contains(r.Status) && r.FirstDay <= to && r.LastDay >= from);
        if (userIds is not null)
        {
            requests = requests.Where(r => userIds.Contains(r.UserId));
        }

        return (await requests.ToListAsync(cancellationToken))
            .Select(r => (r.UserId, Period: PeriodOf(r)))
            .Where(x => x.Period.Takes(from, to))
            .ToList();
    }

    public async Task<IReadOnlyList<TimeOffRequest>> OpenFromAsync(long userId, DateOnly from, CancellationToken cancellationToken = default)
        => await db.TimeOffRequests
            .Where(r => r.UserId == userId
                && (r.Status == TimeOffStatus.Pending || (r.Status == TimeOffStatus.Approved && r.LastDay >= from)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeOffRequest>> PendingStartingByAsync(DateOnly day, CancellationToken cancellationToken = default)
        => await db.TimeOffRequests
            .Include(r => r.User)
            .Where(r => r.Status == TimeOffStatus.Pending && r.FirstDay <= day)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeOffRequest>> UnderWayAsync(DateOnly day, CancellationToken cancellationToken = default)
        => await db.TimeOffRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.Status == TimeOffStatus.Approved && r.FirstDay <= day && r.LastDay >= day)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectMeeting>> MeetingsOfAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await db.ProjectMeetings
            .AsNoTracking()
            .Where(m => m.DeletedAt == null && m.StartsAt < toUtc && m.EndsAt > fromUtc && m.Attendees.Any(a => a.UserId == userId))
            .OrderBy(m => m.StartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> ActiveTeamMembersAsync(long teamId, CancellationToken cancellationToken = default)
        => await db.Users
            .Where(u => u.TeamId == teamId && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public static TimeOffRules.Period PeriodOf(TimeOffRequest r) => new(r.Id, r.FirstDay, r.LastDay, r.Status, r.EndedOn, r.IsPaid);

    private async Task<IReadOnlyList<TimeOffRow>> RowsAsync(IQueryable<TimeOffRequest> requests, CancellationToken cancellationToken)
    {
        var rows = await requests
            .AsNoTracking()
            .Select(r => new
            {
                Request = r,
                r.User.FirstName,
                r.User.LastName,
                r.User.Email,
                r.User.PhotoFileId,
                r.User.TeamId,
                TeamName = r.User.Team != null ? r.User.Team.Name : null,
                DecidedBy = db.Users.Where(u => u.Id == r.DecidedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
                EndedBy = db.Users.Where(u => u.Id == r.EndedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimeOffRow(
                r.Request,
                PersonName.Of(r.FirstName, r.LastName, r.Email),
                r.PhotoFileId,
                r.TeamId,
                r.TeamName,
                r.DecidedBy is null ? null : PersonName.Of(r.DecidedBy.FirstName, r.DecidedBy.LastName, r.DecidedBy.Email),
                r.EndedBy is null ? null : PersonName.Of(r.EndedBy.FirstName, r.EndedBy.LastName, r.EndedBy.Email)))
            .ToList();
    }
}
