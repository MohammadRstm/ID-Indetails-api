using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Proton.Implementation;

public sealed class ProtonActionRepository(IdIndetailsDbContext db) : IProtonActionRepository
{
    public void Add(ProtonAction action) => db.ProtonActions.Add(action);

    public Task<ProtonAction?> FindAsync(Guid publicId, CancellationToken cancellationToken = default)
        => db.ProtonActions.AsNoTracking().SingleOrDefaultAsync(a => a.PublicId == publicId, cancellationToken);

    public Task SupersedeAsync(
        Guid conversationId, string tool, string? targetType, long? targetId, Guid exceptPublicId, DateTime at, CancellationToken cancellationToken = default)
        => db.ProtonActions
            .Where(a => a.ConversationId == conversationId && a.Status == ProtonActionStatus.Pending && a.PublicId != exceptPublicId
                && a.Tool == tool && a.TargetType == targetType && a.TargetId == targetId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ProtonActionStatus.Superseded)
                .SetProperty(a => a.DecidedAt, at), cancellationToken);

    public Task<int> CountPendingAsync(
        Guid conversationId, string tool, string? targetType, long? targetId, DateTime at, CancellationToken cancellationToken = default)
        => db.ProtonActions.CountAsync(a => a.ConversationId == conversationId && a.Status == ProtonActionStatus.Pending && a.ExpiresAt > at
            && !(a.Tool == tool && a.TargetType == targetType && a.TargetId == targetId), cancellationToken);

    // proposed within the last confirmations' reach ((UserId, CreatedAt) index), confirmed since
    public Task<int> CountConfirmedSinceAsync(long userId, DateTime since, CancellationToken cancellationToken = default)
        => db.ProtonActions.CountAsync(a => a.UserId == userId && a.CreatedAt >= since.AddHours(-1) && a.DecidedAt >= since
            && (a.Status == ProtonActionStatus.Executing || a.Status == ProtonActionStatus.Succeeded || a.Status == ProtonActionStatus.Failed), cancellationToken);

    public Task<int> CountDoneSinceAsync(long userId, string tool, DateTime since, CancellationToken cancellationToken = default)
        => db.ProtonActions.CountAsync(a => a.UserId == userId && a.Tool == tool && a.CreatedAt >= since.AddHours(-1) && a.DecidedAt >= since
            && (a.Status == ProtonActionStatus.Executing || a.Status == ProtonActionStatus.Succeeded), cancellationToken);

    public async Task<IReadOnlyList<ProtonAction>> MineAsync(long userId, int take, CancellationToken cancellationToken = default)
        => await db.ProtonActions.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProtonAction>> InConversationAsync(long userId, Guid conversationId, CancellationToken cancellationToken = default)
        => await db.ProtonActions.AsNoTracking().Where(a => a.UserId == userId && a.ConversationId == conversationId).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(ProtonAction Action, string Who)>> AllAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProtonActions.AsNoTracking()
            .Join(db.Users, a => a.UserId, u => u.Id, (a, u) => new { Action = a, u.FirstName, u.LastName, u.Email })
            .OrderByDescending(r => r.Action.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Action, $"{r.FirstName} {r.LastName}".Trim() is { Length: > 0 } name ? name : r.Email)).ToList();
    }

    public async Task<bool> TryClaimAsync(Guid publicId, long userId, DateTime at, CancellationToken cancellationToken = default)
        => await db.ProtonActions
            .Where(a => a.PublicId == publicId && a.UserId == userId && a.Status == ProtonActionStatus.Pending && a.ExpiresAt > at)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ProtonActionStatus.Executing)
                .SetProperty(a => a.DecidedAt, at), cancellationToken) == 1;

    public async Task<bool> CloseAsync(Guid publicId, ProtonActionStatus status, DateTime at, CancellationToken cancellationToken = default)
        => await db.ProtonActions
            .Where(a => a.PublicId == publicId && a.Status == ProtonActionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.DecidedAt, at), cancellationToken) == 1;

    public Task FinishAsync(
        Guid publicId, ProtonActionStatus status, string? resultType, long? resultId, string? errorCode, string? errorMessage,
        DateTime at, CancellationToken cancellationToken = default)
        => db.ProtonActions
            .Where(a => a.PublicId == publicId && a.Status == ProtonActionStatus.Executing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.ResultType, resultType)
                .SetProperty(a => a.ResultId, resultId)
                .SetProperty(a => a.ErrorCode, errorCode)
                .SetProperty(a => a.ErrorMessage, errorMessage)
                .SetProperty(a => a.FinishedAt, at), cancellationToken);
}
