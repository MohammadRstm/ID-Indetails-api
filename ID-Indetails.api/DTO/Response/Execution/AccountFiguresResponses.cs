using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Execution;

/// <summary>The project's own cost figures (<see cref="ProjectCostResponse"/>), arranged for an Execution site's account.</summary>
/// <param name="StaffUsd">Employees' labour (hours logged): the project cost's <c>labour</c>.</param>
/// <param name="OtherUsd">Project expenses: the project cost's <c>expenses</c>.</param>
public sealed record AccountCostsResponse(
    decimal SiteExpensesUsd,
    decimal GroundLabourUsd,
    decimal StaffUsd,
    decimal OtherUsd,
    decimal TotalUsd);

/// <param name="DueUsd">The work price times the supervision rate.</param>
/// <param name="LeftUsd">Due less taken; negative when more was taken than is due.</param>
public sealed record AccountSupervisionResponse(decimal DueUsd, decimal TakenUsd, decimal LeftUsd);

public sealed record AccountPocketResponse(decimal MovedToPocketUsd, decimal PaidByPocketUsd);

/// <param name="AccountLabel">Null for the unlabelled row, which is always last.</param>
/// <param name="ReceivedUsd">Payments filed under the label.</param>
/// <param name="SpentUsd">Site expenses filed under the label. The unlabelled row also holds every cost no label holds: ground labour, staff, other, and expenses with no label.</param>
/// <param name="OtherUsd">On the unlabelled row only: supervision taken plus what was moved to the pocket money, less what it paid back.</param>
/// <param name="RestUsd">Received less spent less other. The rows add up to cash in account.</param>
public sealed record AccountLabelRowResponse(
    AccountLabelRefResponse? AccountLabel,
    decimal ReceivedUsd,
    decimal SpentUsd,
    decimal OtherUsd,
    decimal RestUsd);

public sealed record AccountCategoryRowResponse(SiteExpenseCategory Category, decimal SpentUsd);

/// <param name="Payee">The supplier as it was last typed; null for expenses with none ("No payee").</param>
public sealed record AccountPayeeRowResponse(string? Payee, decimal SpentUsd);

/// <summary>
/// An Execution site's account, in USD at the rate of each day: what the client pays and has paid, what was spent,
/// supervision taken and the cash that should be in the project account. Only for callers with <c>financial:view</c>.
/// </summary>
/// <param name="WorkUsd">The work price, set by agreeing BOQs.</param>
/// <param name="TvaUsd">TVA on the work, never on supervision.</param>
/// <param name="ClientTotalUsd">Work plus supervision plus TVA.</param>
/// <param name="ReceivedUsd">Every live payment, TVA included.</param>
/// <param name="RestToPayUsd">The client's total less received; negative when they paid ahead.</param>
/// <param name="ProfitUsd">The project's profit figure, as on its money.</param>
/// <param name="CashInAccountUsd">Received − cost − supervision taken − moved to the pocket money + paid by it.</param>
/// <param name="ByCategory">Site expenses by category, largest first.</param>
/// <param name="ByPayee">Site expenses by supplier, largest first.</param>
/// <param name="Unconverted">Costs with no exchange rate on their day, in their own currency: left out of every sum above.</param>
public sealed record AccountFiguresResponse(
    decimal WorkUsd,
    decimal SupervisionRate,
    decimal SupervisionUsd,
    decimal TvaRate,
    decimal TvaUsd,
    decimal ClientTotalUsd,
    decimal ReceivedUsd,
    decimal RestToPayUsd,
    AccountCostsResponse Costs,
    decimal ProfitUsd,
    AccountSupervisionResponse Supervision,
    AccountPocketResponse Pocket,
    decimal CashInAccountUsd,
    IReadOnlyList<AccountLabelRowResponse> ByLabel,
    IReadOnlyList<AccountCategoryRowResponse> ByCategory,
    IReadOnlyList<AccountPayeeRowResponse> ByPayee,
    IReadOnlyList<UnconvertedCostResponse> Unconverted);
