namespace ID_Indetails.api.Business.WorkEntries;

/// <summary>
/// When work can still change: from the open month on (<see cref="WorkEntryRules.OpenFrom"/>), and in the closed months
/// a super admin reopened for logging, while they stay open (<see cref="ReopenRules"/>).
/// </summary>
/// <param name="OpenFromUtc">When the open month began, in UTC.</param>
/// <param name="Reopened">The reopened months still open, each from its first moment to the next month's, in UTC.</param>
public sealed record OpenMonths(DateTime OpenFromUtc, IReadOnlyList<OpenMonths.Window> Reopened)
{
    public sealed record Window(DateTime FromUtc, DateTime ToUtc);

    public bool IsOpen(DateTime startUtc) => startUtc >= OpenFromUtc || Reopened.Any(w => startUtc >= w.FromUtc && startUtc < w.ToUtc);
}
