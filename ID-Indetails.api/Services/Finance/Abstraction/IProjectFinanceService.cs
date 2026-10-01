using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Finance.Abstraction;

namespace ID_Indetails.api.Services.Finance.Abstraction;

/// <summary>
/// The shared place every project money figure comes from (see <c>Business/Finance/ProjectFinance</c>).
/// Domain features add their costs to its sources rather than calculating their own.
/// </summary>
public interface IProjectFinanceService
{
    /// <summary>Money of many projects, with one query per cost source whatever the number of projects.</summary>
    Task<IReadOnlyDictionary<long, ProjectFinancials>> GetAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken = default);

    Task<ProjectFinancials> GetAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// The paid breaks of everyone with a pay record on the days of the given work, as labour cost per project and day
    /// (see <c>Business/Finance/PaidBreaks</c>). Loads their other work of those days, since a break is shared across
    /// all the day's projects; the result can therefore include projects outside <paramref name="work"/>.
    /// </summary>
    Task<IReadOnlyList<BreakCost>> PaidBreaksAsync(
        IReadOnlyCollection<WorkEntryRow> work,
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Each project's money plus every cost on its day (in USD, and apart those with no exchange rate that day) and
    /// who worked on which day, for period and month-by-month figures. Same loading as
    /// <see cref="GetAsync(IReadOnlyCollection{Project}, CancellationToken)"/>.
    /// </summary>
    Task<IReadOnlyDictionary<long, ProjectCostHistory>> CostHistoryAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken = default);

    /// <summary>The project's cash position week by week, from its first week to the week of <paramref name="to"/>.</summary>
    Task<IReadOnlyList<ProjectFinance.CashWeek>> CashByWeekAsync(
        Project project,
        DateOnly to,
        DayOfWeek weekStart = DayOfWeek.Monday,
        CancellationToken cancellationToken = default);
}

/// <param name="MaterialCharge">Execution: the client prices, in USD, of the materials sent to the client on a statement: part of revenue.</param>
/// <param name="UnsentMaterialCharge">Execution: the client prices of the materials not sent yet, which a statement will add to revenue.</param>
/// <param name="UnsentMaterials">Execution: how many materials haven't been sent to the client yet.</param>
/// <param name="UnconvertedMaterials">Execution: materials left out of the charge for want of an exchange rate.</param>
/// <param name="UnpricedLabourMinutes">Worked time of people with no pay record, left out of labour cost.</param>
/// <param name="CashProfit">Execution: received − cost.</param>
/// <param name="MaterialProfit">Execution: what the materials sent earn: their charge − their cost.</param>
public sealed record ProjectFinancials(
    ProjectFinance.Summary Summary,
    ProjectFinance.Costs Costs,
    int LabourMinutes,
    int UnpricedLabourMinutes,
    decimal? MaterialCharge,
    int UnconvertedMaterials,
    decimal Received,
    decimal? CashProfit,
    decimal? MaterialProfit,
    decimal? UnsentMaterialCharge = null,
    int UnsentMaterials = 0);

public sealed record WorkDay(long UserId, DateOnly Day, int Minutes);

/// <param name="Unconverted">Costs with no exchange rate on their day, in their own currency.</param>
public sealed record ProjectCostHistory(
    ProjectFinancials Money,
    IReadOnlyList<Recognition.DatedCost> Costs,
    IReadOnlyList<ProjectFinance.DatedUnconverted> Unconverted,
    IReadOnlyList<WorkDay> Work);

public sealed record BreakCost(long UserId, long ProjectId, ProjectFinance.CostItem Cost);
