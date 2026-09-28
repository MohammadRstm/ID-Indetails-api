using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Statements.Abstraction;

/// <summary>A statement in a list: its line count and total, and its deliveries.</summary>
public sealed record StatementListRow(ClientStatement Statement, int Count, decimal Total);

public interface IStatementRepository
{
    /// <summary>The project's statements, newest first, not tracked, with their deliveries.</summary>
    Task<(IReadOnlyList<StatementListRow> Items, int TotalCount)> ListAsync(long projectId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with project, client, lines and deliveries.</summary>
    Task<ClientStatement?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>When the project's latest statement was made; null before the first.</summary>
    Task<DateTime?> LatestCreatedAtAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The number the year's next statement would take, without using it up.</summary>
    Task<int> PeekSequenceAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Takes the year's next number. Call inside a transaction: the sequence row stays locked until it ends.</summary>
    Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Names of the given users, for "made by" and "sent by".</summary>
    Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    void Add(ClientStatement statement);

    void AddDelivery(ClientStatementDelivery delivery);
}
