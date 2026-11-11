using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>
/// A project's cost budget (<see cref="BudgetRules"/>). Every cost comes from the shared finance calculator: the
/// project's costs, Design's labour by department (as the departments window adds it up, never by person here) and a
/// site's expenses by category converted at each day's rate (as its account figures split them).
/// </summary>
public sealed class ProjectBudgetService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectBudgetRepository budgets,
    IProjectDepartmentRepository departments,
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IAccountFiguresRepository siteExpenses,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectBudgetService
{
    private const string NotFoundMessage = "Project not found.";

    public async Task<ProjectBudgetResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<ProjectBudgetResponse> SetAsync(long projectId, SetProjectBudgetRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken))
            .Require(PermissionCodes.FinancialWrite, PermissionCodes.ProjectPricingManage);
        Project project;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // the project's row is held until the version is saved, so two budgets set at once can't share a number
            project = await projects.GetForUpdateAsync(projectId, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
            if (!actor.CanSee(project.DomainCode))
            {
                throw new NotFoundException(NotFoundMessage);
            }

            var writer = project.DomainCode == DomainCodes.Execution ? PermissionCodes.ProjectPricingManage : PermissionCodes.FinancialWrite;
            if (!actor.Has(writer) || !actor.Has(PermissionCodes.FinancialView))
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "Only whoever changes this project's money sets its budget.");
            }

            var lines = request.Lines
                .Select(l => new BudgetRules.Line(l.TeamId, l.Category, l.Amount))
                .ToList();
            IReadOnlyCollection<long> onProject = project.DomainCode == DomainCodes.Design
                ? (await departments.ListAsync([project.Id], cancellationToken)).Select(d => d.TeamId).ToList()
                : [];
            var effectiveFrom = request.EffectiveFrom ?? clock.CompanyToday;
            BudgetRules.Check(project.DomainCode, lines, onProject, effectiveFrom, clock.CompanyToday).ThrowIfDenied();

            var latest = (await budgets.ListAsync(project.Id, cancellationToken)).FirstOrDefault();
            var budget = new ProjectBudget
            {
                ProjectId = project.Id,
                Version = (latest?.Version ?? 0) + 1,
                EffectiveFrom = effectiveFrom,
                Reason = request.Reason.Trim(),
                SetById = actor.UserId,
                SetAt = clock.UtcNow,
                Lines = lines.Select(l => new ProjectBudgetLine { TeamId = l.TeamId, Category = l.Category, AmountUsd = l.Amount }).ToList(),
            };
            budgets.Add(budget);

            // the totals go in the amount columns, which only those who see the money are shown; no person beside them
            activity.Add(new ActivityEvent
            {
                DomainCode = project.DomainCode,
                Kind = ActivityEventKinds.BudgetSet,
                OccurredAt = clock.UtcNow,
                ActorId = actor.UserId,
                ProjectId = project.Id,
                SubjectType = "project",
                SubjectId = project.Id,
                Amount = BudgetRules.TotalOf(lines),
                PrevAmount = latest is null ? null : latest.Lines.Sum(l => l.AmountUsd),
                Payload = JsonSerializer.Serialize(new { version = budget.Version }),
            });

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(project, cancellationToken);
    }

    private async Task<ProjectBudgetResponse> DescribeAsync(Project project, CancellationToken cancellationToken)
    {
        var versions = await budgets.ListAsync(project.Id, cancellationToken);
        var money = await finance.GetAsync(project, cancellationToken);
        var current = versions.FirstOrDefault();
        IReadOnlyList<ProjectBudgetLineRow> lines = current?.Lines ?? [];

        var figures = BudgetRules.Compare(
            lines.Select(l => new BudgetRules.Line(l.TeamId, l.Category, l.AmountUsd)).ToList(),
            await CostsAsync(project, money.Costs, cancellationToken));
        var order = BudgetRules.CategoriesOf(project.DomainCode);
        var rows = lines
            .Zip(figures.Lines, (line, f) => new ProjectBudgetLineResponse(
                f.TeamId,
                f.Category,
                line.TeamName ?? (f.Category is { } category ? BudgetRules.NameOf(category) : "Department"),
                f.Amount,
                f.Cost,
                f.Left))
            .OrderBy(r => r.TeamId is null)
            .ThenBy(r => r.TeamId is null ? string.Empty : r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Category is { } category ? IndexIn(order, category) : 0)
            .ToList();

        return new ProjectBudgetResponse(
            project.Id,
            project.DomainCode,
            current is null ? null : VersionOf(current),
            rows,
            figures.Uncovered,
            money.Costs.Unconverted
                .GroupBy(u => u.Currency.ToUpperInvariant())
                .Select(g => new CurrencyAmountResponse(g.Key, Math.Round(g.Sum(u => u.Amount), 2)))
                .OrderBy(u => u.Currency)
                .ToList(),
            versions.Select(VersionOf).ToList());
    }

    /// <summary>
    /// The project's costs so far, keyed as budget lines hold them. Their sum is the project's cost: what no line of the
    /// business can hold (a site's employee labour, a Design project's site costs, should there be any) is keyed to none.
    /// </summary>
    private async Task<IReadOnlyList<BudgetRules.Cost>> CostsAsync(Project project, ProjectFinance.Costs costs, CancellationToken cancellationToken)
    {
        var result = new List<BudgetRules.Cost>
        {
            new(null, BudgetRules.Expenses, costs.Expenses),
            new(null, BudgetRules.GroundLabour, costs.GroundLabour),
        };

        if (project.DomainCode == DomainCodes.Design)
        {
            // each person's labour rounded as the departments window rounds it, so a department's cost reads the same there
            result.AddRange((await finance.LabourByDepartmentAsync(project, cancellationToken))
                .GroupBy(l => (l.TeamId, l.UserId))
                .Select(g => new BudgetRules.Cost(g.Key.TeamId, null, Math.Round(g.Sum(l => l.Cost), 2))));
            result.Add(new BudgetRules.Cost(null, null, costs.SiteExpenses));
            return result;
        }

        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
        result.AddRange((await siteExpenses.SiteExpensesAsync(project.Id, cancellationToken))
            .Select(e => (e.Category, Usd: rates.ToUsd(e.Total, e.Currency, e.SpentOn)))
            .Where(e => e.Usd is not null)
            .GroupBy(e => e.Category)
            .Select(g => new BudgetRules.Cost(null, BudgetRules.CategoryOf(g.Key), g.Sum(e => e.Usd!.Value))));
        result.Add(new BudgetRules.Cost(null, null, costs.Labour));
        return result;
    }

    private static ProjectBudgetVersionResponse VersionOf(ProjectBudgetRow row)
        => new(row.Version, row.EffectiveFrom, row.Reason, row.SetByName, row.SetAt, row.Lines.Sum(l => l.AmountUsd));

    private static int IndexIn(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return list.Count;
    }
}
