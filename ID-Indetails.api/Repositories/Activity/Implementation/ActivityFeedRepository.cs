using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Activity.Implementation;

public sealed class ActivityFeedRepository(IdIndetailsDbContext db) : IActivityFeedRepository
{
    public async Task<(IReadOnlyList<FeedEvent> Items, int TotalCount)> SearchAsync(
        ActivityFeedSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.ActivityEvents.AsNoTracking()
            .Where(e => (e.DomainCode != null && domains.Contains(e.DomainCode)) || (search.WithCompany && e.DomainCode == null));

        if (search.CompanyOnly)
        {
            query = query.Where(e => e.DomainCode == null);
        }

        if (search.Kinds is { } kinds)
        {
            var kindList = kinds.ToList();
            query = query.Where(e => kindList.Contains(e.Kind));
        }

        if (search.ExcludedKinds.Count > 0)
        {
            var excluded = search.ExcludedKinds.ToList();
            query = query.Where(e => !excluded.Contains(e.Kind));
        }

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(e => e.ProjectId == projectId);
        }

        if (search.ClientId is { } clientId)
        {
            query = query.Where(e => e.ClientId == clientId || db.Projects.Any(p => p.Id == e.ProjectId && p.ClientId == clientId));
        }

        if (search.ActorId is { } actorId)
        {
            query = query.Where(e => e.ActorId == actorId);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        var actorIds = rows.Where(e => e.ActorId != null).Select(e => e.ActorId!.Value).Distinct().ToList();
        var projectIds = rows.Where(e => e.ProjectId != null).Select(e => e.ProjectId!.Value).Distinct().ToList();
        var actors = (await db.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email));
        var projects = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.ClientId, ClientName = p.Client != null ? p.Client.Name : null })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var clientIds = rows.Where(e => e.ClientId != null).Select(e => e.ClientId!.Value).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var items = rows.Select(e =>
        {
            var project = e.ProjectId is { } p ? projects.GetValueOrDefault(p) : null;
            var clientId = e.ClientId ?? project?.ClientId;
            var clientName = e.ClientId is { } c ? clients.GetValueOrDefault(c) : project?.ClientName;
            return new FeedEvent(
                e.Id,
                e.Kind,
                e.DomainCode,
                e.OccurredAt,
                e.ActorId,
                e.ActorId is { } a ? actors.GetValueOrDefault(a) : null,
                e.ProjectId,
                project?.Title,
                clientId,
                clientName,
                e.SubjectType,
                e.SubjectId,
                e.Amount,
                e.PrevAmount,
                e.Payload,
                e.IsDemo);
        }).ToList();

        return (items, total);
    }
}
