using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Payroll.Implementation;

public sealed class SalaryAdvanceRepository(IdIndetailsDbContext db) : ISalaryAdvanceRepository
{
    public void Add(SalaryAdvance advance) => db.SalaryAdvances.Add(advance);

    public void RemoveRepayments(IEnumerable<SalaryAdvanceRepayment> repayments) => db.SalaryAdvanceRepayments.RemoveRange(repayments);

    public async Task<SalaryAdvance?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        var advance = await db.SalaryAdvances
            .FromSql($"SELECT * FROM `salary_advances` WHERE `id` = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (advance is not null)
        {
            await db.Entry(advance).Collection(a => a.Repayments).LoadAsync(cancellationToken);
        }

        return advance;
    }

    public Task<SalaryAdvance?> GetAsync(long id, CancellationToken cancellationToken = default)
        => WithPerson().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalaryAdvance>> OfUserAsync(long userId, CancellationToken cancellationToken = default)
        => await WithPerson()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalaryAdvance>> GivenToAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await WithPerson()
            .Where(a => userIds.Contains(a.UserId) && a.Status == SalaryAdvanceStatus.Approved)
            .OrderBy(a => a.GivenOn)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalaryAdvance>> WithStatusAsync(
        IReadOnlyCollection<SalaryAdvanceStatus> statuses,
        CancellationToken cancellationToken = default)
        => await WithPerson()
            .Where(a => statuses.Contains(a.Status))
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingAsync(long userId, CancellationToken cancellationToken = default)
        => db.SalaryAdvances.AnyAsync(a => a.UserId == userId && a.Status == SalaryAdvanceStatus.Pending, cancellationToken);

    public async Task<IReadOnlyList<AdvanceWriteOff>> WriteOffsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await db.SalaryAdvances
            .AsNoTracking()
            .Where(a => a.SettledKind == SalaryAdvanceSettlement.WrittenOff && a.SettledOn >= from && a.SettledOn <= to)
            .Select(a => new AdvanceWriteOff(a.SettledOn!.Value, a.Currency, a.SettledAmount!.Value))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => (await db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email));

    private IQueryable<SalaryAdvance> WithPerson()
        => db.SalaryAdvances
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Repayments)
            .Include(a => a.User).ThenInclude(u => u.Team)
            .Include(a => a.User).ThenInclude(u => u.EmploymentPeriods);
}
