using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Proton.Abstraction;

public interface IProtonMemoryRepository
{
    void Add(ProtonMemory memory);

    /// <summary>The person's memories, oldest first.</summary>
    Task<IReadOnlyList<ProtonMemory>> MineAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the person's own memories; false when they have none with that id.</summary>
    Task<bool> DeleteAsync(long userId, long id, CancellationToken cancellationToken = default);
}
