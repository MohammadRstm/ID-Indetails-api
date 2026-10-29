using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

/// <summary>
/// Cost, profit, the price and the pocket money's moves come from the shared finance calculator, so they match the
/// project's own money; only the split by label, category and payee is loaded here (<see cref="AccountFiguresRules"/>).
/// </summary>
public sealed class AccountFiguresService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectFinanceService finance,
    IProjectFinanceRepository financeSources,
    IAccountFiguresRepository figures,
    ISupervisionWithdrawalRepository withdrawals,
    IAccountLabelRepository accountLabels) : IAccountFiguresService
{
    public async Task<AccountFiguresResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        if (SupervisionRules.CheckSite(project.DomainCode, project.BillingType) is { } denial)
        {
            throw denial.ToException();
        }

        var money = await finance.GetAsync(project, cancellationToken);
        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
        var taken = await withdrawals.TakenAsync(project.Id, cancellationToken);
        var payments = await figures.LivePaymentsAsync(project.Id, cancellationToken);
        var expenses = await figures.SiteExpensesAsync(project.Id, cancellationToken);
        // read after the rows, so a label one of them names is in the list (labels are archived, never deleted)
        var labels = await accountLabels.ListAsync(includeArchived: true, cancellationToken);
        var supervisionDue = money.Supervision ?? 0;

        var result = AccountFiguresRules.Build(new AccountFiguresRules.Inputs(
            money.RevenueWithTva,
            supervisionDue,
            money.Costs,
            taken,
            money.ToPocket,
            money.FromPocket,
            labels.Select(l => l.Id).ToList(),
            payments.Select(p => new AccountFiguresRules.PaymentRow(p.AccountLabelId, p.AmountUsd)).ToList(),
            expenses
                .Select(e => new AccountFiguresRules.ExpenseRow(e.AccountLabelId, e.Category, e.Supplier, rates.ToUsd(e.Total, e.Currency, e.SpentOn)))
                .ToList()));

        var labelNames = labels.ToDictionary(l => l.Id, l => l.Name);
        return new AccountFiguresResponse(
            project.ProjectFee ?? 0,
            project.SupervisionRate ?? 0,
            supervisionDue,
            project.TvaRate,
            money.RevenueTva,
            money.RevenueWithTva,
            result.Received,
            result.RestToPay,
            new AccountCostsResponse(money.Costs.SiteExpenses, money.Costs.GroundLabour, money.Costs.Labour, money.Costs.Expenses, money.Costs.Total),
            money.Summary.Profit,
            new AccountSupervisionResponse(supervisionDue, taken, result.SupervisionLeft),
            new AccountPocketResponse(money.ToPocket, money.FromPocket),
            result.CashInAccount,
            result.ByLabel
                .Select(r => new AccountLabelRowResponse(
                    r.LabelId is { } id ? new AccountLabelRefResponse(id, labelNames[id]) : null,
                    r.Received,
                    r.Spent,
                    r.Other,
                    r.Rest))
                .ToList(),
            result.ByCategory.Select(c => new AccountCategoryRowResponse(c.Category, c.Spent)).ToList(),
            result.ByPayee.Select(p => new AccountPayeeRowResponse(p.Payee, p.Spent)).ToList(),
            money.Costs.Unconverted.Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount)).ToList());
    }
}
