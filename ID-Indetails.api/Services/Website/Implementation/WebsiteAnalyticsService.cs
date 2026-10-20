using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.GoogleAnalytics.Abstraction;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>
/// The Website area's analytics (docs/website.md, "Analytics"). Google's answers are the same for everyone allowed to
/// see them and hold nothing of any one user, so they are cached for all alike; after Google fails it is left alone
/// for a minute. The days are filled in and the project pages named by their website project.
/// </summary>
public sealed class WebsiteAnalyticsService(
    IGoogleAnalyticsClient google,
    IWebsiteRepository website,
    IMemoryCache cache,
    IClock clock,
    IOptions<GoogleAnalyticsOptions> options) : IWebsiteAnalyticsService
{
    private const string PausedKey = "website:analytics:paused";
    private const string RealtimeKey = "website:analytics:realtime";

    public async Task<WebsiteAnalyticsResponse> ReportAsync(WebsiteAnalyticsQuery query, CancellationToken cancellationToken = default)
    {
        RequireConnected();
        var today = clock.CompanyToday;
        var period = WebsiteAnalyticsRules.Period(query.From, query.To, query.Compare, today);
        var key = $"website:analytics:report:{period.From.DayNumber}:{period.To.DayNumber}:{period.Compared}";
        return (await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = WebsiteAnalyticsRules.CacheLifetime(period.To, today);
            var report = await AskAsync(() => google.ReportAsync(period, cancellationToken));
            return Build(period, report, await website.ProjectTitlesBySlugAsync(cancellationToken));
        }))!;
    }

    public async Task<WebsiteAnalyticsRealtimeResponse> RealtimeAsync(CancellationToken cancellationToken = default)
    {
        RequireConnected();
        return (await cache.GetOrCreateAsync(RealtimeKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = WebsiteAnalyticsRules.RealtimeCacheLifetime;
            var now = await AskAsync(() => google.RealtimeAsync(cancellationToken));
            return new WebsiteAnalyticsRealtimeResponse(now.VisitorsNow, now.VisitorsPerMinute, now.Pages, now.Countries, clock.UtcNow);
        }))!;
    }

    private void RequireConnected()
    {
        if (!options.Value.Enabled)
        {
            throw new ServiceUnavailableException(ErrorCodes.WebsiteAnalyticsNotConfigured, WebsiteAnalyticsRules.NotConfiguredMessage);
        }
    }

    private async Task<T> AskAsync<T>(Func<Task<T>> ask)
    {
        if (cache.TryGetValue(PausedKey, out _))
        {
            throw new ServiceUnavailableException(ErrorCodes.WebsiteAnalyticsUnavailable, WebsiteAnalyticsRules.UnavailableMessage);
        }

        try
        {
            return await ask();
        }
        catch (ServiceUnavailableException)
        {
            cache.Set(PausedKey, true, WebsiteAnalyticsRules.PauseAfterFailure);
            throw;
        }
    }

    private WebsiteAnalyticsResponse Build(AnalyticsPeriod period, GoogleAnalyticsReport report, IReadOnlyDictionary<string, string> titles)
    {
        var contact = report.Events.Where(e => e.Name == WebsiteAnalyticsRules.ContactRequestEvent).ToList();
        var pages = report.Pages
            .Select(p => WebsiteAnalyticsRules.ProjectSlug(p.Path) is { } slug && titles.TryGetValue(slug, out var title) ? p with { ProjectTitle = title } : p)
            .ToList();
        return new WebsiteAnalyticsResponse(
            new WebsiteAnalyticsPeriod(period.From, period.To, period.PreviousFrom, period.PreviousTo),
            HasData: report.Totals.Sessions > 0 || report.Totals.Views > 0 || report.Totals.Events > 0,
            report.Thresholded,
            report.TimeZone,
            clock.UtcNow,
            WebsiteAnalyticsRules.Totals(report.Totals, contact.Sum(e => e.Count)),
            report.PreviousTotals is { } previous ? WebsiteAnalyticsRules.Totals(previous, contact.Sum(e => e.PreviousCount ?? 0)) : null,
            WebsiteAnalyticsRules.Days(period, report.Days, report.PreviousDays),
            report.Channels,
            report.Sources,
            pages,
            report.Countries,
            report.Devices,
            report.VisitorTypes,
            report.Events);
    }
}
