namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>A work entry, as far as its overtime is concerned.</summary>
public sealed record LoggedWork(long Id, long UserId, DateTime StartedAt);

/// <summary>
/// The overtime of logged work, split by <see cref="Business.Payroll.Overtime"/> exactly as pay splits it. An entry's
/// overtime depends on the rest of its person's work that day, so their whole days are loaded.
/// </summary>
public interface IOvertimeService
{
    /// <summary>The overtime minutes of these entries by id; entries with none are left out.</summary>
    Task<IReadOnlyDictionary<long, int>> ByEntryAsync(IReadOnlyCollection<LoggedWork> work, CancellationToken cancellationToken = default);
}
