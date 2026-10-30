using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Boqs.Abstraction;

/// <summary>A BOQ in a list: its deliveries, and how many lines it has.</summary>
public sealed record BoqListRow(Boq Boq, int LineCount);

/// <summary>What the project's agreed BOQs add up to: the client BOQ's total (0 when none is agreed) and each agreed VO's.</summary>
public sealed record AgreedBoqs(bool ClientAgreed, decimal ClientTotal, IReadOnlyList<decimal> VoTotals);

public interface IBoqRepository
{
    /// <summary>The project's live BOQs, newest first, not tracked, with their deliveries.</summary>
    Task<IReadOnlyList<BoqListRow>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live BOQ, not tracked, with its project, lines and deliveries.</summary>
    Task<Boq?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>A live BOQ, tracked, with its lines and deliveries. Call after locking the project.</summary>
    Task<Boq?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>What the project's live, agreed BOQs add up to. Call after locking the project.</summary>
    Task<AgreedBoqs> AgreedAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Takes the year's next number for the prefix. Call inside a transaction: the sequence row stays locked until it ends.</summary>
    Task<int> NextSequenceAsync(string prefix, int year, CancellationToken cancellationToken = default);

    /// <summary>Names of the given users, for "by" and "sent by".</summary>
    Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    void Add(Boq boq);

    void AddDelivery(BoqDelivery delivery);
}
