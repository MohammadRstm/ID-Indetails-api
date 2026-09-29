using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
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

    public async Task<IReadOnlyList<ProjectFinance.PayRate>> PayRatesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.UserPayHistory
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new ProjectFinance.PayRate(p.UserId, p.EffectiveFrom, p.HourlyCostRate, p.Currency, p.Basis == PayBasis.Hourly))
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
            .Select(m => new MaterialRow(m.ProjectId, m.Currency, m.Total, m.ClientPrice, m.AddedAt))
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
