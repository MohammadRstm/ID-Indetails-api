using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Payroll.Abstraction;

/// <summary>A written-off rest of an advance: a company expense on its day.</summary>
public sealed record AdvanceWriteOff(DateOnly Day, string Currency, decimal Amount);

/// <summary>Salary advances and the months they come off. Advances are loaded with their months and the person.</summary>
public interface ISalaryAdvanceRepository
{
    void Add(SalaryAdvance advance);

    void RemoveRepayments(IEnumerable<SalaryAdvanceRepayment> repayments);

    /// <summary>The advance with its months, tracked and locked (<c>SELECT … FOR UPDATE</c>).</summary>
    Task<SalaryAdvance?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The advance with its months and the person (their team and periods of employment), not tracked.</summary>
    Task<SalaryAdvance?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Someone's advances, every status, newest first.</summary>
    Task<IReadOnlyList<SalaryAdvance>> OfUserAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>The advances given to these people (settled ones too, not cancelled ones), oldest first.</summary>
    Task<IReadOnlyList<SalaryAdvance>> GivenToAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>Every advance in these statuses, whoever it is for.</summary>
    Task<IReadOnlyList<SalaryAdvance>> WithStatusAsync(IReadOnlyCollection<SalaryAdvanceStatus> statuses, CancellationToken cancellationToken = default);

    Task<bool> HasPendingAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>The rests written off from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    Task<IReadOnlyList<AdvanceWriteOff>> WriteOffsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The names of these people, by id, for who decided, recorded or settled an advance.</summary>
    Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);
}
