using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Models;

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

    /// <summary>The project's cash position week by week, from its first week to the week of <paramref name="to"/>.</summary>
    Task<IReadOnlyList<ProjectFinance.CashWeek>> CashByWeekAsync(Project project, DateOnly to, CancellationToken cancellationToken = default);
}

/// <param name="MaterialCharge">Execution: material client prices in USD.</param>
/// <param name="UnconvertedMaterials">Execution: materials left out of the charge for want of an exchange rate.</param>
/// <param name="UnpricedLabourMinutes">Worked time of people with no pay record, left out of labour cost.</param>
/// <param name="CashProfit">Execution: received − cost.</param>
/// <param name="MaterialProfit">Execution: material charge − material cost.</param>
public sealed record ProjectFinancials(
    ProjectFinance.Summary Summary,
    ProjectFinance.Costs Costs,
    int LabourMinutes,
    int UnpricedLabourMinutes,
    decimal? MaterialCharge,
    int UnconvertedMaterials,
    decimal Received,
    decimal? CashProfit,
    decimal? MaterialProfit);
