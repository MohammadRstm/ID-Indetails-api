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

    public Task SupersedeAsync(Guid conversationId, Guid exceptPublicId, DateTime at, CancellationToken cancellationToken = default)
        => db.ProtonActions
            .Where(a => a.ConversationId == conversationId && a.Status == ProtonActionStatus.Pending && a.PublicId != exceptPublicId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ProtonActionStatus.Superseded)
                .SetProperty(a => a.DecidedAt, at), cancellationToken);

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
