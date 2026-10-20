using ID_Indetails.api.Business.Website;
using ID_Indetails.api.DTO.Response.Website;

namespace ID_Indetails.api.Services.GoogleAnalytics.Abstraction;

/// <summary>
/// The company website's Google Analytics 4 property, read with the Data API (docs/website.md, "Analytics"). The only
/// part of the API that talks to Google; tests use a fake. A failure is logged with Google's reason and thrown as a 503
/// <c>website_analytics_unavailable</c>.
/// </summary>
public interface IGoogleAnalyticsClient
{
    /// <summary>Every report of the Analytics page for the period, and for the previous one when compared.</summary>
    Task<GoogleAnalyticsReport> ReportAsync(AnalyticsPeriod period, CancellationToken cancellationToken = default);

    /// <summary>The last 30 minutes.</summary>
    Task<GoogleAnalyticsRealtime> RealtimeAsync(CancellationToken cancellationToken = default);
}

/// <summary>What Google answered for a period, before its days are filled in and its project pages named.</summary>
/// <param name="Thresholded">Google left out rows with very few visitors.</param>
public sealed record GoogleAnalyticsReport(
    AnalyticsCounts Totals,
    AnalyticsCounts? PreviousTotals,
    IReadOnlyList<AnalyticsDayCounts> Days,
    IReadOnlyList<AnalyticsDayCounts> PreviousDays,
    IReadOnlyList<WebsiteAnalyticsChannel> Channels,
    IReadOnlyList<WebsiteAnalyticsSource> Sources,
    IReadOnlyList<WebsiteAnalyticsPage> Pages,
    IReadOnlyList<WebsiteAnalyticsCountry> Countries,
    IReadOnlyList<WebsiteAnalyticsShare> Devices,
    IReadOnlyList<WebsiteAnalyticsShare> VisitorTypes,
    IReadOnlyList<WebsiteAnalyticsEvent> Events,
    bool Thresholded,
    string? TimeZone);

/// <summary>The last 30 minutes. <paramref name="VisitorsPerMinute"/> has 30 values, oldest first.</summary>
public sealed record GoogleAnalyticsRealtime(
    long VisitorsNow,
    IReadOnlyList<long> VisitorsPerMinute,
    IReadOnlyList<WebsiteRealtimePage> Pages,
    IReadOnlyList<WebsiteRealtimeCountry> Countries);
