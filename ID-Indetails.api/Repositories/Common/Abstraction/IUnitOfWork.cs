namespace ID_Indetails.api.Repositories.Common.Abstraction;

/// <summary>
/// Commits the changes staged by repositories, and lets a service group several steps
/// (including non-database ones like sending an email) into one all-or-nothing transaction.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves, unless a unique index refuses a new row because another request saved the same one a moment ago: then
    /// everything staged is dropped and it returns false, so the caller can read that row instead. Stage only that row.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether anything is staged and not saved yet.</summary>
    bool HasChanges { get; }

    /// <summary>Drops everything staged and not saved, so a later save can't write it.</summary>
    void DiscardChanges();

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
