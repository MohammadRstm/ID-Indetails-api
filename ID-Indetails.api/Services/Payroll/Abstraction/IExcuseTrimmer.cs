namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>New work gives way to an excuse that stood in for it (see <c>Business/Payroll/ExcuseTrim</c>).</summary>
public interface IExcuseTrimmer
{
    /// <summary>
    /// After work from <paramref name="startUtc"/> to <paramref name="endUtc"/> was saved for the person: their excuses of
    /// that month give way where the work replaces them, kept in history. Runs in the caller's transaction.
    /// </summary>
    Task TrimAsync(long userId, DateTime startUtc, DateTime endUtc, long actorId, CancellationToken cancellationToken = default);
}
