using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class SupervisionWithdrawalRepository(IdIndetailsDbContext db) : ISupervisionWithdrawalRepository
{
    public async Task<IReadOnlyList<SupervisionWithdrawalRow>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => (await RowsAsync(db.SupervisionWithdrawals.Where(w => w.ProjectId == projectId && w.DeletedAt == null), cancellationToken))
            .OrderByDescending(r => r.Withdrawal.TakenOn)
            .ThenByDescending(r => r.Withdrawal.Id)
            .ToList();

    public async Task<SupervisionWithdrawalRow?> GetRowAsync(long id, CancellationToken cancellationToken = default)
        => (await RowsAsync(db.SupervisionWithdrawals.Where(w => w.Id == id), cancellationToken)).SingleOrDefault();

    public async Task<SupervisionWithdrawal?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => (await db.SupervisionWithdrawals
                .FromSql($"SELECT * FROM `supervision_withdrawals` WHERE `id` = {id} AND `deleted_at` IS NULL FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public async Task<decimal> TakenAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.SupervisionWithdrawals
            .Where(w => w.ProjectId == projectId && w.DeletedAt == null)
            .SumAsync(w => (decimal?)w.AmountUsd, cancellationToken) ?? 0;

    public void Add(SupervisionWithdrawal withdrawal) => db.SupervisionWithdrawals.Add(withdrawal);

    /// <summary>Each with the name of who recorded it.</summary>
    private async Task<IReadOnlyList<SupervisionWithdrawalRow>> RowsAsync(
        IQueryable<SupervisionWithdrawal> withdrawals,
        CancellationToken cancellationToken)
    {
        var rows = await (from w in withdrawals.AsNoTracking()
                          join u in db.Users on w.CreatedById equals u.Id
                          select new { Withdrawal = w, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new SupervisionWithdrawalRow(r.Withdrawal, PersonName.Of(r.FirstName, r.LastName, r.Email))).ToList();
    }
}
