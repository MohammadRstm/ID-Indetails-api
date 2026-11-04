using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>What a project's applied variation orders add up to, in all and for each department.</summary>
public sealed record AppliedDesignVos(decimal Total, IReadOnlyDictionary<long, decimal> ByDepartment)
{
    public static readonly AppliedDesignVos None = new(0, new Dictionary<long, decimal>());
}

/// <summary>Design variation orders, with their lines and each department's part.</summary>
public interface IDesignVoRepository
{
    /// <summary>A project's VOs, newest first.</summary>
    Task<IReadOnlyList<DesignVo>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    Task<DesignVo?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its lines and allocations, to change.</summary>
    Task<DesignVo?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The next number of the year in Design's series, locked until the transaction ends.</summary>
    Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default);

    Task<AppliedDesignVos> AppliedAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>When the project's latest VO was applied; null when none was.</summary>
    Task<DateTime?> LastAppliedAtAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Minutes logged on the project outside its agreed scope since <paramref name="sinceUtc"/>.</summary>
    Task<int> OutsideScopeMinutesAsync(long projectId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    void Add(DesignVo vo);
}
