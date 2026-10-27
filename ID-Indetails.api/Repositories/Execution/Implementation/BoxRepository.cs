using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class BoxRepository(IdIndetailsDbContext db) : IBoxRepository
{
    public Task LockAsync(CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `id` FROM `company_settings` FOR UPDATE", cancellationToken);

    public async Task<decimal> BalanceAsync(CancellationToken cancellationToken = default)
        => await db.BoxEntries.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

    public async Task<decimal> ContributedAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.BoxEntries
            .Where(e => e.ProjectId == projectId && e.Kind != BoxEntryKind.Transfer)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

    public Task<BoxEntry?> LastRemovalAsync(long projectId, CancellationToken cancellationToken = default)
        => db.BoxEntries.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.Kind == BoxEntryKind.Removed)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> HoldsPaymentAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        var kinds = await db.BoxEntries.Where(e => e.PaymentId == paymentId).Select(e => e.Kind).ToListAsync(cancellationToken);
        return kinds.Count(k => k == BoxEntryKind.PaymentIn) > kinds.Count(k => k == BoxEntryKind.PaymentVoided);
    }

    public async Task<IReadOnlyList<BoxProjectRow>> ProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.DomainCode == DomainCodes.Execution && (p.Status != ProjectStatus.Archived || p.BoxState != BoxState.Removed))
            .OrderBy(p => p.Title).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        var ids = projects.Select(p => p.Id).ToList();
        var sums = await db.BoxEntries
            .Where(e => ids.Contains(e.ProjectId))
            .GroupBy(e => new { e.ProjectId, IsTransfer = e.Kind == BoxEntryKind.Transfer })
            .Select(g => new { g.Key.ProjectId, g.Key.IsTransfer, Amount = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);
        return projects.Select(p => new BoxProjectRow(
            p,
            p.Client is { } client ? ClientRules.DisplayName(client.Name, client.Organisation) : null,
            sums.Where(s => s.ProjectId == p.Id && !s.IsTransfer).Sum(s => s.Amount),
            -sums.Where(s => s.ProjectId == p.Id && s.IsTransfer).Sum(s => s.Amount))).ToList();
    }

    public async Task<(IReadOnlyList<BoxEntryRow> Items, int TotalCount)> SearchAsync(BoxEntrySearch search, CancellationToken cancellationToken = default)
    {
        var query = db.BoxEntries.AsNoTracking();
        if (search.ProjectId is { } id)
        {
            query = query.Where(e => e.ProjectId == id);
        }

        if (search.Kind is { } kind)
        {
            query = query.Where(e => e.Kind == kind);
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
        var items = await Rows(
            query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize),
            cancellationToken);
        return (items, total);
    }

    public async Task<BoxEntryRow?> GetAsync(long id, CancellationToken cancellationToken = default)
        => (await Rows(db.BoxEntries.AsNoTracking().Where(e => e.Id == id), cancellationToken)).SingleOrDefault();

    public void Add(BoxEntry entry) => db.BoxEntries.Add(entry);

    private async Task<IReadOnlyList<BoxEntryRow>> Rows(IQueryable<BoxEntry> entries, CancellationToken cancellationToken)
    {
        var rows = await entries
            .Select(e => new
            {
                Entry = e,
                e.Project.Title,
                By = db.Users.Where(u => u.Id == e.CreatedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new BoxEntryRow(r.Entry, r.Title, r.By is { } by ? PersonName.Of(by.FirstName, by.LastName, by.Email) : null)).ToList();
    }
}
