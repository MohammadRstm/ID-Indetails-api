using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.GoogleAnalytics.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Google Analytics for the tests: answers set by the test, calls counted, and a switch to fail. Never the internet.</summary>
public sealed class FakeGoogleAnalytics : IGoogleAnalyticsClient
{
    public static readonly AnalyticsCounts NoCounts = new(0, 0, 0, 0, 0, 0, 0, 0);

    public static readonly GoogleAnalyticsReport NothingRecorded = new(NoCounts, NoCounts, [], [], [], [], [], [], [], [], [], false, "Asia/Beirut");

    public GoogleAnalyticsReport Report { get; set; } = NothingRecorded;

    public GoogleAnalyticsRealtime Realtime { get; set; } = new(0, new long[30], [], []);

    /// <summary>Answers as Google does when it fails.</summary>
    public bool Down { get; set; }

    /// <summary>The periods asked for, in order.</summary>
    public List<AnalyticsPeriod> Periods { get; } = [];

    public int RealtimeCalls { get; private set; }

    public void Reset()
    {
        Report = NothingRecorded;
        Realtime = new(0, new long[30], [], []);
        Down = false;
        Periods.Clear();
        RealtimeCalls = 0;
    }

    public Task<GoogleAnalyticsReport> ReportAsync(AnalyticsPeriod period, CancellationToken cancellationToken = default)
    {
        Periods.Add(period);
        return Down ? throw Unavailable() : Task.FromResult(Report);
    }

    public Task<GoogleAnalyticsRealtime> RealtimeAsync(CancellationToken cancellationToken = default)
    {
        RealtimeCalls++;
        return Down ? throw Unavailable() : Task.FromResult(Realtime);
    }

    private static ServiceUnavailableException Unavailable()
        => new(ErrorCodes.WebsiteAnalyticsUnavailable, WebsiteAnalyticsRules.UnavailableMessage);
}
