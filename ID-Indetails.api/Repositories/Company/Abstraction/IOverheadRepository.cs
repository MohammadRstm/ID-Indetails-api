using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

public interface IOverheadRepository
{
    /// <summary>Every category, first to last, with how many lines use each.</summary>
    Task<IReadOnlyList<(OverheadGroup Group, int Lines)>> GroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<OverheadGroup?> GetGroupForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Whether another category has this name (the database compares names without case).</summary>
    Task<bool> GroupNameTakenAsync(string name, long? exceptId, CancellationToken cancellationToken = default);

    Task<int> LinesInGroupAsync(long id, CancellationToken cancellationToken = default);

    void AddGroup(OverheadGroup group);

    void RemoveGroup(OverheadGroup group);

    /// <summary>Every line with its amounts and category, not tracked.</summary>
    Task<IReadOnlyList<OverheadItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its amounts and category.</summary>
    Task<OverheadItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    void Add(OverheadItem item);

    void AddAmount(OverheadAmount amount);

    void Remove(OverheadItem item);
}
