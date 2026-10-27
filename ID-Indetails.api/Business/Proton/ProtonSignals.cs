using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Proton;

/// <summary>
/// The approved thresholds (Proton:Insights), only these let Proton call a change notable or unusual, and the existing
/// Design report rules for idle and close-out projects.
/// </summary>
public sealed record ProtonThresholds(
    int NotablePct,
    decimal NotableHours,
    decimal NotableUsd,
    decimal UnusualHigh,
    decimal UnusualLow,
    int BaselinePeriods,
    int TargetSoonDays,
    int IdleDays,
    int CloseoutProgress);

/// <summary>What an attention signal is about, most pressing first: the order is the ranking.</summary>
public enum ProtonSignalKind
{
    OverdueInvoice,
    FinishedOwed,
    ProjectOverdue,
    OverdueRequirements,
    CostHigh,
    NoPayment,
    TargetSoon,
    HoursUnusual,
    HoursChange,
    CostChange,
    Idle,
    Closeout,
    OnHold,
    NotLogging,
}

/// <param name="ProjectId">Null for signals about a whole domain (people not logging).</param>
/// <param name="Weight">Orders signals of one kind: days or an amount, bigger first.</param>
/// <param name="Fact">The measured facts, in words.</param>
/// <param name="Rule">The rule that fired, in words.</param>
public sealed record ProtonSignal(ProtonSignalKind Kind, long? ProjectId, string? ProjectName, string? Domain, decimal Weight, string Fact, string Rule);

/// <summary>Everything that fired for one project (or one domain-wide signal), in ranking order.</summary>
public sealed record ProtonSignalGroup(long? ProjectId, string? ProjectName, string? Domain, IReadOnlyList<ProtonSignal> Signals);

/// <summary>
/// The rules behind "what should I look at?". Each reuses an existing business rule where there is one (idle, close-out,
/// cost warning, overdue) and the approved thresholds otherwise; nothing here judges a person.
/// </summary>
public static class ProtonSignals
{
    public static string IdleRule(ProtonThresholds t) => $"an active Design project with no work logged for {t.IdleDays} days or more";

    public static string CloseoutRule(ProtonThresholds t) => $"an active project at {t.CloseoutProgress}% progress or more";

    public static string CostHighRule => $"cost past {MoneyAttention.CostWarning * 100:0}% of the agreed revenue";

    public const string OverdueRule = "an active or on-hold project whose target date has passed";

    public static string TargetSoonRule(ProtonThresholds t) => $"an active project with its target date within {t.TargetSoonDays} days";

    public static string NotableHoursRule(ProtonThresholds t) => $"a change of {t.NotablePct}% or more and at least {t.NotableHours:0.#} h";

    public static string NotableMoneyRule(ProtonThresholds t) => $"a change of {t.NotablePct}% or more and at least {t.NotableUsd:0} USD";

    public static string UnusualRule(ProtonThresholds t)
        => $"{t.UnusualHigh:0.##}× or more, or {t.UnusualLow:0.##}× or less, of the average of the {t.BaselinePeriods} periods before, and at least {t.NotableHours:0.#} h from it";

    /// <summary>
    /// A change big enough to call notable: at least <see cref="ProtonThresholds.NotablePct"/> and
    /// <paramref name="floor"/>. From nothing, the floor alone decides.
    /// </summary>
    public static bool IsNotable(ProtonChange change, decimal floor, ProtonThresholds t)
        => Math.Abs(change.Diff) >= floor && (change.Pct is not { } pct || Math.Abs(pct) >= t.NotablePct);

    /// <summary>Far enough from a <see cref="ProtonTrends.Baseline"/> to call unusual; never without a baseline.</summary>
    public static bool IsUnusual(decimal current, decimal? baseline, decimal floor, ProtonThresholds t)
        => baseline is { } b && Math.Abs(current - b) >= floor && (current >= b * t.UnusualHigh || current <= b * t.UnusualLow);

    public static bool IsTargetSoon(DateOnly? targetDate, ProjectStatus status, DateOnly today, ProtonThresholds t)
        => status == ProjectStatus.Active && targetDate is { } target && target >= today && target.DayNumber - today.DayNumber <= t.TargetSoonDays;

    public static bool IsIdle(int daysSinceWork, ProtonThresholds t) => daysSinceWork >= t.IdleDays;

    public static bool IsCloseout(ProjectStatus status, int progressPct, ProtonThresholds t) => status == ProjectStatus.Active && progressPct >= t.CloseoutProgress;

    /// <summary>
    /// One entry per project with all of its signals, ranked by its most pressing signal and then by weight; at most
    /// <paramref name="limit"/>, and how many more there were.
    /// </summary>
    public static (IReadOnlyList<ProtonSignalGroup> Groups, int More) Rank(IEnumerable<ProtonSignal> signals, int limit)
    {
        var groups = signals
            .GroupBy(s => s.ProjectId is { } id ? $"p{id}" : $"{s.Kind}:{s.Domain}")
            .Select(g =>
            {
                var ordered = g.OrderBy(s => s.Kind).ThenByDescending(s => s.Weight).ToList();
                return new ProtonSignalGroup(ordered[0].ProjectId, ordered[0].ProjectName, ordered[0].Domain, ordered);
            })
            .OrderBy(g => g.Signals[0].Kind)
            .ThenByDescending(g => g.Signals[0].Weight)
            .ThenBy(g => g.ProjectId ?? long.MaxValue)
            .ToList();
        return (groups.Take(limit).ToList(), Math.Max(0, groups.Count - limit));
    }
}
