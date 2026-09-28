using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Common.Abstraction;
using Microsoft.EntityFrameworkCore.Storage;

namespace ID_Indetails.api.Repositories.Common.Implementation;

public sealed class UnitOfWork(IdIndetailsDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private sealed class Transaction(IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
