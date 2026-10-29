using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class PocketRepository(IdIndetailsDbContext db) : IPocketRepository
{
    public Task LockAsync(CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `id` FROM `company_settings` FOR UPDATE", cancellationToken);

    public async Task<decimal> BalanceAsync(CancellationToken cancellationToken = default)
        => await db.PocketEntries.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

    public async Task<(decimal In, decimal Out)> TotalsAsync(CancellationToken cancellationToken = default)
    {
        var contributed = await db.PocketEntries.Where(e => e.Amount > 0).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;
        var paidOut = await db.PocketEntries.Where(e => e.Amount < 0).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;
        return (contributed, -paidOut);
    }

    public async Task<(IReadOnlyList<PocketEntryRow> Items, int TotalCount)> SearchAsync(PocketEntrySearch search, CancellationToken cancellationToken = default)
    {
        var query = db.PocketEntries.AsNoTracking();
        if (search.Kind is { } kind)
        {
            query = query.Where(e => e.Kind == kind);
        }

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(e => e.ProjectId == projectId);
        }

        if (search.FromUtc is { } from)
        {
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(e => e.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            foreach (var word in search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Where(e => e.Project.Title.Contains(word)
                    || (e.Note != null && e.Note.Contains(word))
                    || db.Users.Any(u => u.Id == e.CreatedById
                        && ((u.FirstName != null && u.FirstName.Contains(word)) || (u.LastName != null && u.LastName.Contains(word)) || u.Email.Contains(word))));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(e => new
            {
                Entry = e,
                e.Project.Title,
                By = db.Users.Where(u => u.Id == e.CreatedById).Select(u => new { u.FirstName, u.LastName, u.Email }).First(),
            })
            .ToListAsync(cancellationToken);
        return (rows.Select(r => new PocketEntryRow(r.Entry, r.Title, PersonName.Of(r.By.FirstName, r.By.LastName, r.By.Email))).ToList(), total);
    }

    public void Add(PocketEntry entry) => db.PocketEntries.Add(entry);

    public Task<IReadOnlyList<PocketRequestRow>> PendingAsync(CancellationToken cancellationToken = default)
        => RequestRows(db.PocketRequests.AsNoTracking().Where(r => r.Status == PocketRequestStatus.Pending).OrderBy(r => r.RequestedAt).ThenBy(r => r.Id), cancellationToken);

    public async Task<PocketRequestRow?> GetRequestAsync(long id, CancellationToken cancellationToken = default)
        => (await RequestRows(db.PocketRequests.AsNoTracking().Where(r => r.Id == id), cancellationToken)).SingleOrDefault();

    public Task<PocketRequest?> GetRequestForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.PocketRequests
            .FromSql($"SELECT * FROM `pocket_requests` WHERE `id` = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<PocketRequest?> PendingOfAsync(long projectId, CancellationToken cancellationToken = default)
        => db.PocketRequests.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Status == PocketRequestStatus.Pending, cancellationToken);

    public void Add(PocketRequest request) => db.PocketRequests.Add(request);

    private async Task<IReadOnlyList<PocketRequestRow>> RequestRows(IQueryable<PocketRequest> requests, CancellationToken cancellationToken)
    {
        var rows = await requests
            .Select(r => new
            {
                Request = r,
                r.Project.Title,
                RequestedBy = db.Users.Where(u => u.Id == r.RequestedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
                DecidedBy = db.Users.Where(u => u.Id == r.DecidedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new PocketRequestRow(
            r.Request,
            r.Title,
            r.RequestedBy is { } by ? PersonName.Of(by.FirstName, by.LastName, by.Email) : null,
            r.DecidedBy is { } decider ? PersonName.Of(decider.FirstName, decider.LastName, decider.Email) : null)).ToList();
    }
}
