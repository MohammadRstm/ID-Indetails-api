using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Proton.Abstraction;

/// <param name="Questions">Turns recorded, whatever their outcome; voice sessions are not questions.</param>
/// <param name="CostUsd">Questions and voice sessions together.</param>
public sealed record ProtonUserUsage(long UserId, string Name, int Questions, decimal CostUsd, int Tokens);

/// <param name="Domain">The person's domain now.</param>
/// <param name="Tools">The turn's recorded tool calls (JSON).</param>
public sealed record ProtonTurnRow(long UserId, string? Domain, Guid ConversationId, DateTime OccurredAt, decimal CostUsd, string Tools);

/// <param name="Seconds">Voice time as recorded, reported or estimated.</param>
/// <param name="Estimated">Sessions whose final time the provider never reported.</param>
public sealed record ProtonVoiceTotals(int Sessions, int Seconds, decimal CostUsd, int Estimated);

public sealed record ProtonTopUpRow(long Id, decimal AmountUsd, string? Note, string AddedBy, DateTime AddedAt);

public interface IProtonUsageRepository
{
    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonUsage usage);

    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonBudgetTopUp topUp);

    /// <summary>Estimated cost of every question between the two instants (UTC, end exclusive).</summary>
    Task<decimal> SpentAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The questions that count toward the monthly allowance: those Proton used a tool for. Small talk answered
    /// without one costs the budget but not the allowance.</summary>
    Task<int> QuestionsAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<decimal> TopUpTotalAsync(DateOnly month, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProtonTopUpRow>> TopUpsAsync(DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Per person, most expensive first.</summary>
    Task<IReadOnlyList<ProtonUserUsage>> ByUserAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Every question between the two instants, oldest first, for the budget breakdown (no voice sessions).</summary>
    Task<IReadOnlyList<ProtonTurnRow>> TurnsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The voice sessions between the two instants, apart from the questions asked in them.</summary>
    Task<ProtonVoiceTotals> VoiceAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
