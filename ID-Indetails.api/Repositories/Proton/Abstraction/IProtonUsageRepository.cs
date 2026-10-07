using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Proton.Abstraction;

/// <param name="Questions">Turns recorded, whatever their outcome.</param>
public sealed record ProtonUserUsage(long UserId, string Name, int Questions, decimal CostUsd, int Tokens);

/// <param name="Domain">The person's domain now.</param>
/// <param name="Tools">The turn's recorded tool calls (JSON).</param>
public sealed record ProtonTurnRow(long UserId, string? Domain, Guid ConversationId, DateTime OccurredAt, decimal CostUsd, string Tools);

public sealed record ProtonTopUpRow(long Id, decimal AmountUsd, string? Note, string AddedBy, DateTime AddedAt);

public interface IProtonUsageRepository
{
    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonUsage usage);

    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonBudgetTopUp topUp);

    /// <summary>Estimated cost of every question between the two instants (UTC, end exclusive).</summary>
    Task<decimal> SpentAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<int> QuestionsAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<decimal> TopUpTotalAsync(DateOnly month, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProtonTopUpRow>> TopUpsAsync(DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Per person, most expensive first.</summary>
    Task<IReadOnlyList<ProtonUserUsage>> ByUserAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Every question between the two instants, oldest first, for the budget breakdown.</summary>
    Task<IReadOnlyList<ProtonTurnRow>> TurnsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
