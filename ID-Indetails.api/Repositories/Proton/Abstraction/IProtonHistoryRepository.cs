using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Proton.Abstraction;

/// <summary>People's saved chats with Proton. Every read is by owner: no method returns another person's chat.</summary>
public interface IProtonHistoryRepository
{
    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonStoredConversation conversation);

    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonStoredMessage message);

    /// <summary>The person's chat, tracked for changes; null when they have none with that id.</summary>
    Task<ProtonStoredConversation?> FindAsync(long userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The person's chats, most recently used first, optionally those whose title contains <paramref name="search"/>.</summary>
    Task<(IReadOnlyList<ProtonStoredConversation> Items, int Total)> ListAsync(
        long userId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>A chat's messages in order (the caller has checked whose it is).</summary>
    Task<IReadOnlyList<ProtonStoredMessage>> MessagesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A chat's last <paramref name="count"/> messages, in order.</summary>
    Task<IReadOnlyList<ProtonStoredMessage>> LastMessagesAsync(Guid id, int count, CancellationToken cancellationToken = default);

    /// <summary>Deletes the person's chat and its messages; false when they have none with that id.</summary>
    Task<bool> DeleteAsync(long userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Chats nobody has touched since <paramref name="before"/>, oldest first.</summary>
    Task<IReadOnlyList<Guid>> UntouchedSinceAsync(DateTime before, int take, CancellationToken cancellationToken = default);

    /// <summary>Deletes these chats and their messages, whoever they belong to (the retention job's sweep).</summary>
    Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blanks the question and answer text these chats left in the usage ledger, keeping their cost, so a deleted chat's
    /// words don't outlive it.
    /// </summary>
    Task<int> ClearUsageTextAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
