using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

public interface IOverheadRepository
{
    /// <summary>Every line with its amounts, not tracked.</summary>
    Task<IReadOnlyList<OverheadItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its amounts.</summary>
    Task<OverheadItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    void Add(OverheadItem item);

    void AddAmount(OverheadAmount amount);

    void Remove(OverheadItem item);
}
