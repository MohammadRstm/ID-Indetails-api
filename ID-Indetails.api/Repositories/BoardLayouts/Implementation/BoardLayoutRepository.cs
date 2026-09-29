using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.BoardLayouts.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.BoardLayouts.Implementation;

public sealed class BoardLayoutRepository(IdIndetailsDbContext db) : IBoardLayoutRepository
{
    public Task<UserBoardLayout?> GetAsync(long userId, string boardKey, CancellationToken cancellationToken = default)
        => db.UserBoardLayouts.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId && l.BoardKey == boardKey, cancellationToken);

    public Task SaveAsync(long userId, string boardKey, string layout, DateTime savedAt, CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `user_board_layouts` (`user_id`, `board_key`, `layout`, `saved_at`) VALUES ({userId}, {boardKey}, {layout}, {savedAt}) AS new
            ON DUPLICATE KEY UPDATE `layout` = new.`layout`, `saved_at` = new.`saved_at`
            """,
            cancellationToken);

    public Task DeleteAsync(long userId, string boardKey, CancellationToken cancellationToken = default)
        => db.UserBoardLayouts.Where(l => l.UserId == userId && l.BoardKey == boardKey).ExecuteDeleteAsync(cancellationToken);
}
