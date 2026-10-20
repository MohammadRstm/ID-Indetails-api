namespace ID_Indetails.api.DTO.Response.Website;

// The Website area's analytics (docs/website.md, "Analytics"): Google Analytics 4 reports for the company website, in
// the app's own words. Visitors are GA's active users; rates are fractions (0.42); durations are in seconds.

/// <param name="TimeZone">The Google Analytics property's time zone, which its days follow.</param>
/// <param name="Thresholded">Google left out rows with very few visitors, to protect their privacy.</param>
/// <param name="HasData">False while Google has recorded nothing for the period, e.g. a website not yet live.</param>
public sealed record WebsiteAnalyticsResponse(
    WebsiteAnalyticsPeriod Period,
    bool HasData,
    bool Thresholded,
    string? TimeZone,
    DateTime RetrievedAt,
    WebsiteAnalyticsTotals Totals,
    WebsiteAnalyticsTotals? PreviousTotals,
    IReadOnlyList<WebsiteAnalyticsDay> Days,
    IReadOnlyList<WebsiteAnalyticsChannel> Channels,
    IReadOnlyList<WebsiteAnalyticsSource> Sources,
    IReadOnlyList<WebsiteAnalyticsPage> Pages,
    IReadOnlyList<WebsiteAnalyticsCountry> Countries,
    IReadOnlyList<WebsiteAnalyticsShare> Devices,
    IReadOnlyList<WebsiteAnalyticsShare> VisitorTypes,
    IReadOnlyList<WebsiteAnalyticsEvent> Events);

/// <summary>The period, and the one of the same length just before it when compared.</summary>
public sealed record WebsiteAnalyticsPeriod(DateOnly From, DateOnly To, DateOnly? PreviousFrom, DateOnly? PreviousTo);

/// <param name="AverageEngagementSeconds">Time the website was in front of a visitor, per visitor.</param>
/// <param name="ContactRequests">Messages sent from the contact form (the <c>generate_lead</c> event).</param>
public sealed record WebsiteAnalyticsTotals(
    long Visitors,
    long NewVisitors,
    long Sessions,
    long Views,
    double ViewsPerSession,
    double EngagementRate,
    double AverageEngagementSeconds,
    long Events,
    long KeyEvents,
    long ContactRequests);

/// <summary>One day of the period; the previous period's day in the same position, when compared.</summary>
public sealed record WebsiteAnalyticsDay(
    DateOnly Date,
    long Visitors,
    long Sessions,
    long Views,
    DateOnly? PreviousDate,
    long? PreviousVisitors,
    long? PreviousSessions,
    long? PreviousViews);

/// <param name="Channel">Google's default channel group: Organic Search, Direct, Referral, Organic Social…</param>
public sealed record WebsiteAnalyticsChannel(string Channel, long Sessions, long Visitors, double EngagementRate, long KeyEvents);

/// <param name="Campaign">"(direct)", "(organic)" or "(referral)" without a tagged campaign.</param>
public sealed record WebsiteAnalyticsSource(
    string Source, string Medium, string Campaign, long Sessions, long Visitors, double EngagementRate, long KeyEvents);

/// <param name="ProjectTitle">The website project of a <c>/projects/{slug}</c> page, while it has that address.</param>
public sealed record WebsiteAnalyticsPage(
    string Path, string Title, string? ProjectTitle, long Views, long Visitors, double AverageEngagementSeconds, long Events);

/// <param name="Code">ISO 3166-1 alpha-2, or "(not set)".</param>
public sealed record WebsiteAnalyticsCountry(string Code, string Name, long Visitors, long Sessions);

/// <summary>A part of the visitors: a device ("desktop", "mobile", "tablet") or "new" and "returning".</summary>
public sealed record WebsiteAnalyticsShare(string Key, long Visitors, long Sessions);

/// <param name="IsKeyEvent">Marked as a key event in Google Analytics (contact requests, email and phone clicks).</param>
public sealed record WebsiteAnalyticsEvent(string Name, long Count, long? PreviousCount, long Users, bool IsKeyEvent);

/// <summary>The last 30 minutes, a few minutes behind at most. <paramref name="VisitorsPerMinute"/> has 30 values, oldest first.</summary>
public sealed record WebsiteAnalyticsRealtimeResponse(
    long VisitorsNow,
    IReadOnlyList<long> VisitorsPerMinute,
    IReadOnlyList<WebsiteRealtimePage> Pages,
    IReadOnlyList<WebsiteRealtimeCountry> Countries,
    DateTime RetrievedAt);

/// <param name="Title">Realtime reports name pages by their title only.</param>
public sealed record WebsiteRealtimePage(string Title, long Views, long Visitors);

public sealed record WebsiteRealtimeCountry(string Code, string Name, long Visitors);
