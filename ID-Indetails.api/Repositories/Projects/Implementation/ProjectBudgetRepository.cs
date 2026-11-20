using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class ProjectBudgetRepository(IdIndetailsDbContext db) : IProjectBudgetRepository
{
    public async Task<IReadOnlyList<ProjectBudgetRow>> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var budgets = await db.ProjectBudgets
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.Version)
            .Select(b => new
            {
                b.Id,
                b.Version,
                b.EffectiveFrom,
                b.Reason,
                b.SetAt,
                By = db.Users.Where(u => u.Id == b.SetById).Select(u => new { u.FirstName, u.LastName, u.Email }).First(),
            })
            .ToListAsync(cancellationToken);
        if (budgets.Count == 0)
        {
            return [];
        }

        var ids = budgets.Select(b => b.Id).ToList();
        var lines = (await (
                from l in db.ProjectBudgetLines.AsNoTracking()
                where ids.Contains(l.BudgetId)
                join t in db.Teams on l.TeamId equals (long?)t.Id into teams
                from t in teams.DefaultIfEmpty()
                orderby l.Id
                select new { l.BudgetId, Row = new ProjectBudgetLineRow(l.TeamId, t == null ? null : t.Name, l.Category, l.AmountUsd) })
            .ToListAsync(cancellationToken))
            .ToLookup(l => l.BudgetId, l => l.Row);

        return budgets
            .Select(b => new ProjectBudgetRow(
                b.Version,
                b.EffectiveFrom,
                b.Reason,
                PersonName.Of(b.By.FirstName, b.By.LastName, b.By.Email),
                b.SetAt,
                lines[b.Id].ToList()))
            .ToList();
    }

    public Task<bool> HasTeamAsync(long teamId, CancellationToken cancellationToken = default)
        => db.ProjectBudgetLines.AnyAsync(l => l.TeamId == teamId, cancellationToken);

    public void Add(ProjectBudget budget) => db.ProjectBudgets.Add(budget);
}
