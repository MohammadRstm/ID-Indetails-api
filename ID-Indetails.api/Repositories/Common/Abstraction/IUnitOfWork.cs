namespace ID_Indetails.api.Repositories.Common.Abstraction;

/// <summary>
/// Commits the changes staged by repositories, and lets a service group several steps
/// (including non-database ones like sending an email) into one all-or-nothing transaction.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a transaction. Disposing it without <see cref="IUnitOfWorkTransaction.CommitAsync"/>
    /// rolls everything back.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
