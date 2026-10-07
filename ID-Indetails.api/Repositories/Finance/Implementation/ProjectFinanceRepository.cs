using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Finance.Implementation;

public sealed class ProjectFinanceRepository(IdIndetailsDbContext db) : IProjectFinanceRepository
{
    public async Task<IReadOnlyList<ExchangeRateTable.Rate>> ExchangeRatesAsync(CancellationToken cancellationToken = default)
        => await db.ExchangeRates
            .AsNoTracking()
            .Select(r => new ExchangeRateTable.Rate(r.Currency, r.EffectiveFrom, r.RatePerUsd))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkEntryRow>> WorkEntriesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => projectIds.Contains(e.ProjectId) && e.DeletedAt == null)
            .Select(e => new WorkEntryRow(e.ProjectId, e.UserId, e.StartedAt, e.DurationMinutes))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The pay that prices each person's time on projects. A super admin's costs nothing: their salary is a company
    /// cost, counted once on the company overview, and pricing their hours would also give it away.
    /// </summary>
    public async Task<IReadOnlyList<ProjectFinance.PayRecord>> PayHistoryAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.UserPayHistory
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            // admins and super admins are paid a fixed salary, a company cost (the company overview's net profit): any hours
            // they logged cost their projects nothing, so their pay is never counted twice
            .Select(p => new ProjectFinance.PayRecord(p.UserId, p.EffectiveFrom, p.Basis, p.User.RoleCode != RoleCodes.Employee ? 0m : p.Amount, p.Currency))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpenseRow>> ExpensesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.ProjectExpenses
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId) && x.DeletedAt == null)
            .Select(x => new ExpenseRow(x.ProjectId, x.Currency, x.Amount, x.SpentOn))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialRow>> MaterialsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.ProjectMaterials
            .AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) && m.DeletedAt == null)
            .Select(m => new MaterialRow(m.ProjectId, m.Currency, m.Total, m.ClientPrice, m.AddedAt,
                db.ClientStatementLines.Where(l => l.MaterialId == m.Id).SelectMany(l => l.Statement.Deliveries).Min(d => (DateTime?)d.SentAt)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GroundLabourRow>> GroundLabourAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.LabourWeekDays
            .AsNoTracking()
            .Where(d => projectIds.Contains(d.LabourWeek.ProjectId))
            .Select(d => new GroundLabourRow(d.LabourWeek.ProjectId, d.WorkDate, d.Cost))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(DateOnly ReceivedOn, decimal AmountUsd)>> PaymentsAsync(long projectId, CancellationToken cancellationToken = default)
        => (await db.Payments
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId && p.VoidedAt == null)
                .Select(p => new { p.ReceivedOn, p.AmountUsd })
                .ToListAsync(cancellationToken))
            .Select(p => (p.ReceivedOn, p.AmountUsd))
            .ToList();

    public async Task<IReadOnlyDictionary<long, decimal>> ReceivedAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
        => await db.Payments
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.ProjectId) && p.VoidedAt == null)
            .GroupBy(p => p.ProjectId)
            .Select(g => new { ProjectId = g.Key, Received = g.Sum(p => p.AmountUsd) })
            .ToDictionaryAsync(r => r.ProjectId, r => r.Received, cancellationToken);
}
