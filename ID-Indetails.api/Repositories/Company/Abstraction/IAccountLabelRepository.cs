using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

public interface IAccountLabelRepository
{
    /// <summary>First to last; archived ones only when asked for. Tracked, so the caller may change them.</summary>
    Task<IReadOnlyList<AccountLabel>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<AccountLabel?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Whether another label, archived or not, has this name in any case.</summary>
    Task<bool> NameTakenAsync(string name, long? exceptId, CancellationToken cancellationToken = default);

    void Add(AccountLabel label);
}
