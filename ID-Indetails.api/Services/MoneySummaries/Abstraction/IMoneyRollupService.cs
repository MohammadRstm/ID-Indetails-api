using ID_Indetails.api.Models;
using ID_Indetails.api.Services.MoneySummaries.Implementation;

namespace ID_Indetails.api.Services.MoneySummaries.Abstraction;

/// <summary>Loads everything the money summaries need for a set of projects, in a fixed number of queries.</summary>
public interface IMoneyRollupService
{
    Task<MoneyRollup> LoadAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken = default);
}
