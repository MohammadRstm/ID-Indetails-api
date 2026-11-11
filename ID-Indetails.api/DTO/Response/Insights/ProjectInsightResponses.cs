using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Insights;

/// <summary>How a result was worked out: the method, what it assumed and what it left out, so nothing reads as a guess.</summary>
public sealed record InsightBasisResponse(string Method, IReadOnlyList<string> Assumptions, IReadOnlyList<string> LeftOut);

/// <param name="Amount">USD, net of TVA: positive adds to profit, negative takes from it.</param>
public sealed record BridgeStepResponse(BridgeStepKind Kind, decimal Amount);

/// <summary>A Design department's part of the project and the labour logged in it. Never a person's cost.</summary>
/// <param name="Id">Null for the hours of people in no department, or in one no longer on the project.</param>
/// <param name="Share">Its part of the price, a fraction (0.25 is 25%); null for the other hours.</param>
/// <param name="LabourShare">Its share of the project's labour cost, a fraction, while there is any.</param>
public sealed record DepartmentInsightResponse(
    long? Id,
    string Name,
    decimal? Share,
    decimal Budget,
    decimal Cost,
    decimal Left,
    int Minutes,
    decimal? LabourShare,
    IReadOnlyList<CurrencyAmountResponse> Unconverted);

/// <summary>
/// One project's profitability, all time, in USD net of TVA: agreed revenue, cost by source and agreed profit (with a
/// site's pocket money), where that profit comes from, its standing against the profit floor, and Design's departments.
/// </summary>
/// <param name="AgreedWithTva">What the client is agreed to pay, TVA included.</param>
/// <param name="Standing">Against the profit floor; null where it doesn't apply (internal work, no agreed amount yet).</param>
/// <param name="Received">What the client paid, TVA included; <paramref name="ReceivedTva"/> is the TVA in it.</param>
/// <param name="Owed">What the client still owes, TVA included.</param>
/// <param name="SiteCashProfit">A site's all-time received net of TVA − cost − moved to the pocket + paid by it.</param>
/// <param name="Bridge">From the base price to the agreed profit; the steps add up to it.</param>
/// <param name="Departments">Design only.</param>
public sealed record ProjectInsightResponse(
    long ProjectId,
    string Title,
    string Domain,
    ProjectStatus Status,
    BillingType BillingType,
    decimal AgreedRevenue,
    decimal AgreedWithTva,
    ProjectCostResponse Cost,
    decimal AgreedProfit,
    decimal? Margin,
    bool MarginKnown,
    ProfitStanding? Standing,
    int ProgressPct,
    decimal RecognisedToDate,
    ProjectProjectionResponse? Projected,
    decimal Received,
    decimal ReceivedTva,
    decimal Owed,
    decimal? SiteCashProfit,
    decimal ToPocket,
    decimal FromPocket,
    decimal? SupervisionRate,
    decimal TvaRate,
    IReadOnlyList<BridgeStepResponse> Bridge,
    IReadOnlyList<DepartmentInsightResponse>? Departments,
    InsightBasisResponse Basis);

/// <param name="AgreedWithTva">What the client would be agreed to pay, TVA included.</param>
/// <param name="Owed">What the client would still owe, TVA included.</param>
public sealed record ScenarioFiguresResponse(
    decimal AgreedRevenue,
    decimal AgreedWithTva,
    decimal Cost,
    decimal AgreedProfit,
    decimal? Margin,
    decimal Owed,
    ProfitStanding? Standing);

/// <summary>One project's figures today and with the changes asked for. Nothing is saved.</summary>
/// <param name="FloorWouldRefuse">
/// As real changes, the ones other than a settlement would be refused by the profit floor (and kept as a blocked
/// change): they take profit to zero or below and lower it. A settlement isn't held to the floor.
/// </param>
/// <param name="WeeklyCost">With a delay: the weekly cost it was priced at, the average of the last four full weeks.</param>
public sealed record ProjectScenarioResponse(
    long ProjectId,
    ScenarioFiguresResponse Before,
    ScenarioFiguresResponse After,
    decimal ProfitChange,
    bool FloorWouldRefuse,
    decimal? WeeklyCost,
    InsightBasisResponse Basis);
