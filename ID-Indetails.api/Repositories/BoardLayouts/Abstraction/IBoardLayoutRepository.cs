using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.BoardLayouts.Abstraction;

public interface IBoardLayoutRepository
{
    /// <summary>Not tracked.</summary>
    Task<UserBoardLayout?> GetAsync(long userId, string boardKey, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the layout in one statement, so two saves at once can't collide. Runs immediately.</summary>
    Task SaveAsync(long userId, string boardKey, string layout, DateTime savedAt, CancellationToken cancellationToken = default);

    /// <summary>Runs immediately; nothing happens when none is saved.</summary>
    Task DeleteAsync(long userId, string boardKey, CancellationToken cancellationToken = default);
}
