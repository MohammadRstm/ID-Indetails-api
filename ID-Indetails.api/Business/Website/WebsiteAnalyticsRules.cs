using ID_Indetails.api.DTO.Response.Website;

namespace ID_Indetails.api.Business.Website;

/// <summary>A period of the website's analytics, and the one compared with it.</summary>
public sealed record AnalyticsPeriod(DateOnly From, DateOnly To, DateOnly? PreviousFrom, DateOnly? PreviousTo)
{
    public bool Compared => PreviousFrom is not null;

    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>What Google counted over a period, before anything is worked out from it.</summary>
public sealed record AnalyticsCounts(
    long Visitors, long NewVisitors, long Sessions, long EngagedSessions, long Views, double EngagementSeconds, long Events, long KeyEvents);

/// <summary>One day's counts.</summary>
public sealed record AnalyticsDayCounts(DateOnly Date, long Visitors, long Sessions, long Views);

/// <summary>
/// The rules of the website's analytics (docs/website.md, "Analytics"): which period is shown and compared, how long
/// an answer is kept, and the figures worked out from Google's counts.
/// </summary>
public static class WebsiteAnalyticsRules
{
    /// <summary>Without dates, the 28 days ending yesterday: Google Analytics' own default.</summary>
    public const int DefaultDays = 28;

    public const int MaxDays = 366;

    /// <summary>Google Analytics 4 holds nothing earlier.</summary>
    public static readonly DateOnly FirstDay = new(2015, 8, 14);

    /// <summary>Google may still add to a day for about a week; older periods change no more.</summary>
    public const int SettlingDays = 7;

    public static readonly TimeSpan RecentCacheLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SettledCacheLifetime = TimeSpan.FromHours(6);
    public static readonly TimeSpan RealtimeCacheLifetime = TimeSpan.FromMinutes(1);

    /// <summary>After Google fails, it is left alone this long: its server errors count against an hourly quota.</summary>
    public static readonly TimeSpan PauseAfterFailure = TimeSpan.FromMinutes(1);

    /// <summary>The event the website sends for a contact message the API accepted.</summary>
    public const string ContactRequestEvent = "generate_lead";

    public const string NotConfiguredMessage = "Google Analytics isn't connected yet.";
    public const string UnavailableMessage = "Google Analytics didn't answer. Try again in a few minutes.";

    /// <summary>
    /// The period asked for (both days or neither, already validated), ending today at the latest, and the period of
    /// the same length just before it when compared.
    /// </summary>
    public static AnalyticsPeriod Period(DateOnly? from, DateOnly? to, bool compare, DateOnly today)
    {
        var end = to is null ? today.AddDays(-1) : to.Value < today ? to.Value : today;
        var start = from ?? end.AddDays(-(DefaultDays - 1));
        // a period that starts in the future shows today
        if (start > end)
        {
            start = end;
        }

        var days = end.DayNumber - start.DayNumber + 1;
        return compare ? new(start, end, start.AddDays(-days), start.AddDays(-1)) : new(start, end, null, null);
    }

    /// <summary>A recent period is asked again after a few minutes; a settled one keeps for hours.</summary>
    public static TimeSpan CacheLifetime(DateOnly to, DateOnly today)
        => to.DayNumber >= today.DayNumber - SettlingDays ? RecentCacheLifetime : SettledCacheLifetime;

    public static double Ratio(double part, double whole) => whole <= 0 ? 0 : part / whole;

    public static WebsiteAnalyticsTotals Totals(AnalyticsCounts counts, long contactRequests) => new(
        counts.Visitors,
        counts.NewVisitors,
        counts.Sessions,
        counts.Views,
        Ratio(counts.Views, counts.Sessions),
        Ratio(counts.EngagedSessions, counts.Sessions),
        Ratio(counts.EngagementSeconds, counts.Visitors),
        counts.Events,
        counts.KeyEvents,
        contactRequests);

    /// <summary>
    /// Every day of the period, a day Google has no row for counting zero, each beside the previous period's day in
    /// the same position when compared.
    /// </summary>
    public static IReadOnlyList<WebsiteAnalyticsDay> Days(
        AnalyticsPeriod period, IEnumerable<AnalyticsDayCounts> current, IEnumerable<AnalyticsDayCounts> previous)
    {
        var now = current.ToDictionary(d => d.Date);
        var before = previous.ToDictionary(d => d.Date);
        return Enumerable.Range(0, period.Days).Select(i =>
        {
            var date = period.From.AddDays(i);
            var day = now.GetValueOrDefault(date);
            if (period.PreviousFrom is not { } previousFrom)
            {
                return new WebsiteAnalyticsDay(date, day?.Visitors ?? 0, day?.Sessions ?? 0, day?.Views ?? 0, null, null, null, null);
            }

            var previousDate = previousFrom.AddDays(i);
            var earlier = before.GetValueOrDefault(previousDate);
            return new WebsiteAnalyticsDay(
                date, day?.Visitors ?? 0, day?.Sessions ?? 0, day?.Views ?? 0,
                previousDate, earlier?.Visitors ?? 0, earlier?.Sessions ?? 0, earlier?.Views ?? 0);
        }).ToList();
    }

    /// <summary>The website project a page address is, as the website writes it (<c>/projects/{slug}</c>), or null.</summary>
    public static string? ProjectSlug(string path)
    {
        const string prefix = "/projects/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var slug = path[prefix.Length..].TrimEnd('/');
        return slug.Length == 0 || slug.Contains('/') ? null : Uri.UnescapeDataString(slug);
    }
}
