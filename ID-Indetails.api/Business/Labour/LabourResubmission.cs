namespace ID_Indetails.api.Business.Labour;

/// <summary>What a submitted labour week does to the stored one.</summary>
public enum LabourSubmission
{
    /// <summary>Saved: a new report, or a change to the one stored.</summary>
    Save,

    /// <summary>The stored days and note already equal it: nothing is saved, and no event or notification is sent.</summary>
    Unchanged,

    /// <summary>Someone changed the week since the sender loaded it, and the sender's days or note differ (409).</summary>
    Conflict,
}

/// <summary>
/// The phone app submits labour weeks offline, so a week can be sent twice, or after someone else changed it. A week's
/// version (opaque to the app) changes whenever the week does; the app sends the version it loaded.
/// </summary>
public static class LabourResubmission
{
    public const string UnreportedVersion = "0";

    /// <summary>"0" until reported, then the time of its last change in milliseconds.</summary>
    public static string VersionOf(DateTime? submittedAt, DateTime? updatedAt)
        => (updatedAt ?? submittedAt) is { } changed
            ? (WholeMilliseconds(changed).Ticks / TimeSpan.TicksPerMillisecond).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : UnreportedVersion;

    /// <summary>
    /// When a change to a week is recorded: now, in whole milliseconds as the database keeps it, and always after the
    /// previous change, so the version moves on every change.
    /// </summary>
    public static DateTime ChangedAt(DateTime previous, DateTime now)
    {
        var at = WholeMilliseconds(now);
        var after = WholeMilliseconds(previous).AddMilliseconds(1);
        return at >= after ? at : after;
    }

    /// <summary>The same days, workers and daily pay, and the same note (blank counts as none).</summary>
    public static bool SameReport(IReadOnlyCollection<LabourRules.Day> stored, string? storedNote, IReadOnlyCollection<LabourRules.Day> submitted, string? submittedNote)
        => Note(storedNote) == Note(submittedNote)
            && stored.Count == submitted.Count
            && stored.OrderBy(d => d.Date).SequenceEqual(submitted.OrderBy(d => d.Date));

    /// <param name="expectedVersion">The version the sender loaded; null from the website, which doesn't send one.</param>
    public static LabourSubmission Decide(bool reported, bool sameReport, string currentVersion, string? expectedVersion)
    {
        if (reported && sameReport)
        {
            return LabourSubmission.Unchanged;
        }

        return expectedVersion is not null && expectedVersion != currentVersion ? LabourSubmission.Conflict : LabourSubmission.Save;
    }

    private static string? Note(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static DateTime WholeMilliseconds(DateTime at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond, at.Kind);
}
