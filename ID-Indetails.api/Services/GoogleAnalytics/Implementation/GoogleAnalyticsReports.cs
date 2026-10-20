using System.Globalization;
using Google.Apis.AnalyticsData.v1beta.Data;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Services.GoogleAnalytics.Abstraction;

namespace ID_Indetails.api.Services.GoogleAnalytics.Implementation;

/// <summary>
/// The reports the Analytics page is made of, and how Google's answers are read. Each combination of dimensions and
/// metrics is one of Google Analytics' own standard reports. Columns are found by name, never by position.
/// </summary>
public static class GoogleAnalyticsReports
{
    private const string Current = "current";
    private const string Previous = "previous";
    /// <summary>The column Google adds when a report has more than one date range: the range's name.</summary>
    private const string DateRangeColumn = "dateRange";

    private static readonly AnalyticsCounts NoCounts = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Totals, days, channels, sources and pages. Google takes at most five reports in one batch.</summary>
    public static BatchRunReportsRequest Traffic(AnalyticsPeriod period)
    {
        var compared = Ranges(period);
        var current = Ranges(period with { PreviousFrom = null, PreviousTo = null });
        return new BatchRunReportsRequest
        {
            Requests =
            [
                Report(compared, [], ["activeUsers", "newUsers", "sessions", "engagedSessions", "screenPageViews", "userEngagementDuration", "eventCount", "keyEvents"], quota: true),
                Report(compared, ["date"], ["activeUsers", "sessions", "screenPageViews"]),
                Report(current, ["sessionDefaultChannelGroup"], ["sessions", "activeUsers", "engagedSessions", "keyEvents"], orderBy: "sessions", limit: 20),
                Report(current, ["sessionSource", "sessionMedium", "sessionCampaignName"], ["sessions", "activeUsers", "engagedSessions", "keyEvents"], orderBy: "sessions", limit: 25),
                Report(current, ["pagePath", "pageTitle"], ["screenPageViews", "activeUsers", "userEngagementDuration", "eventCount"], orderBy: "screenPageViews", limit: 50),
            ],
        };
    }

    /// <summary>Countries, devices, new and returning visitors, and events.</summary>
    public static BatchRunReportsRequest Audience(AnalyticsPeriod period)
    {
        var current = Ranges(period with { PreviousFrom = null, PreviousTo = null });
        return new BatchRunReportsRequest
        {
            Requests =
            [
                Report(current, ["countryId", "country"], ["activeUsers", "sessions"], orderBy: "activeUsers", limit: 25),
                Report(current, ["deviceCategory"], ["activeUsers", "sessions"], orderBy: "activeUsers"),
                Report(current, ["newVsReturning"], ["activeUsers", "sessions"], orderBy: "activeUsers"),
                Report(Ranges(period), ["eventName"], ["eventCount", "totalUsers", "keyEvents"], orderBy: "eventCount", limit: 100),
            ],
        };
    }

    /// <summary>Reads the answers to <see cref="Traffic"/> and <see cref="Audience"/>, in the order they were asked.</summary>
    public static GoogleAnalyticsReport Read(AnalyticsPeriod period, BatchRunReportsResponse traffic, BatchRunReportsResponse audience)
    {
        var t = traffic.Reports ?? [];
        var a = audience.Reports ?? [];
        var totals = new Table(t[0]);
        var days = new Table(t[1]);

        return new GoogleAnalyticsReport(
            Counts(totals, Current),
            period.Compared ? Counts(totals, Previous) : null,
            Days(days, Current),
            period.Compared ? Days(days, Previous) : [],
            new Table(t[2]).Rows().Select(r => new WebsiteAnalyticsChannel(
                r.Text("sessionDefaultChannelGroup"), r.Long("sessions"), r.Long("activeUsers"), Engagement(r), r.Long("keyEvents"))).ToList(),
            new Table(t[3]).Rows().Select(r => new WebsiteAnalyticsSource(
                r.Text("sessionSource"), r.Text("sessionMedium"), r.Text("sessionCampaignName"),
                r.Long("sessions"), r.Long("activeUsers"), Engagement(r), r.Long("keyEvents"))).ToList(),
            new Table(t[4]).Rows().Select(r => new WebsiteAnalyticsPage(
                r.Text("pagePath"), r.Text("pageTitle"), null, r.Long("screenPageViews"), r.Long("activeUsers"),
                WebsiteAnalyticsRules.Ratio(r.Double("userEngagementDuration"), r.Long("activeUsers")), r.Long("eventCount"))).ToList(),
            new Table(a[0]).Rows().Select(r => new WebsiteAnalyticsCountry(r.Text("countryId"), r.Text("country"), r.Long("activeUsers"), r.Long("sessions"))).ToList(),
            Shares(new Table(a[1]), "deviceCategory"),
            Shares(new Table(a[2]), "newVsReturning"),
            Events(new Table(a[3]), period.Compared),
            t.Concat(a).Any(r => r.Metadata?.SubjectToThresholding == true),
            t[0].Metadata?.TimeZone);
    }

    /// <summary>Visitors per minute, all visitors, the pages being read and the countries, over the last 30 minutes.</summary>
    public static IReadOnlyList<RunRealtimeReportRequest> Realtime() =>
    [
        new() { Dimensions = Dimensions(["minutesAgo"]), Metrics = Metrics(["activeUsers"]), Limit = 30 },
        new() { Metrics = Metrics(["activeUsers"]) },
        new() { Dimensions = Dimensions(["unifiedScreenName"]), Metrics = Metrics(["screenPageViews", "activeUsers"]), OrderBys = ByMetric("screenPageViews"), Limit = 10 },
        new() { Dimensions = Dimensions(["countryId", "country"]), Metrics = Metrics(["activeUsers"]), OrderBys = ByMetric("activeUsers"), Limit = 10 },
    ];

    /// <summary>Reads the answers to <see cref="Realtime"/>, in the order they were asked.</summary>
    public static GoogleAnalyticsRealtime ReadRealtime(IReadOnlyList<RunRealtimeReportResponse> answers)
    {
        var perMinute = new long[30];
        foreach (var row in new Table(answers[0]).Rows())
        {
            if (int.TryParse(row.Text("minutesAgo"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ago) && ago is >= 0 and < 30)
            {
                perMinute[29 - ago] = row.Long("activeUsers");
            }
        }

        return new GoogleAnalyticsRealtime(
            new Table(answers[1]).Rows().Sum(r => r.Long("activeUsers")),
            perMinute,
            new Table(answers[2]).Rows().Select(r => new WebsiteRealtimePage(r.Text("unifiedScreenName"), r.Long("screenPageViews"), r.Long("activeUsers"))).ToList(),
            new Table(answers[3]).Rows().Select(r => new WebsiteRealtimeCountry(r.Text("countryId"), r.Text("country"), r.Long("activeUsers"))).ToList());
    }

    private static List<DateRange> Ranges(AnalyticsPeriod period)
    {
        List<DateRange> ranges = [Range(period.From, period.To, Current)];
        if (period is { PreviousFrom: { } from, PreviousTo: { } to })
        {
            ranges.Add(Range(from, to, Previous));
        }

        return ranges;
    }

    private static DateRange Range(DateOnly from, DateOnly to, string name) => new()
    {
        StartDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EndDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Name = name,
    };

    private static RunReportRequest Report(
        List<DateRange> ranges, string[] dimensions, string[] metrics, string? orderBy = null, long? limit = null, bool quota = false) => new()
    {
        DateRanges = ranges,
        Dimensions = dimensions.Length == 0 ? null : Dimensions(dimensions),
        Metrics = Metrics(metrics),
        OrderBys = orderBy is null ? null : ByMetric(orderBy),
        Limit = limit,
        ReturnPropertyQuota = quota ? true : null,
    };

    private static List<Dimension> Dimensions(string[] names) => names.Select(n => new Dimension { Name = n }).ToList();

    private static List<Metric> Metrics(string[] names) => names.Select(n => new Metric { Name = n }).ToList();

    private static List<OrderBy> ByMetric(string name) => [new OrderBy { Metric = new MetricOrderBy { MetricName = name }, Desc = true }];

    private static AnalyticsCounts Counts(Table totals, string range)
        => totals.Rows(range).Select(r => new AnalyticsCounts(
            r.Long("activeUsers"), r.Long("newUsers"), r.Long("sessions"), r.Long("engagedSessions"), r.Long("screenPageViews"),
            r.Double("userEngagementDuration"), r.Long("eventCount"), r.Long("keyEvents"))).FirstOrDefault() ?? NoCounts;

    private static List<AnalyticsDayCounts> Days(Table days, string range)
        => days.Rows(range).Select(r => new AnalyticsDayCounts(
            DateOnly.ParseExact(r.Text("date"), "yyyyMMdd", CultureInfo.InvariantCulture), r.Long("activeUsers"), r.Long("sessions"),
            r.Long("screenPageViews"))).ToList();

    private static double Engagement(TableRow row) => WebsiteAnalyticsRules.Ratio(row.Long("engagedSessions"), row.Long("sessions"));

    private static List<WebsiteAnalyticsShare> Shares(Table table, string dimension)
        => table.Rows().Select(r => new WebsiteAnalyticsShare(r.Text(dimension), r.Long("activeUsers"), r.Long("sessions"))).ToList();

    /// <summary>This period's events, with the previous period's count beside each when compared.</summary>
    private static List<WebsiteAnalyticsEvent> Events(Table events, bool compared)
    {
        var before = compared ? events.Rows(Previous).ToDictionary(r => r.Text("eventName"), r => r.Long("eventCount")) : null;
        return events.Rows(Current).Select(r => new WebsiteAnalyticsEvent(
            r.Text("eventName"), r.Long("eventCount"), before is null ? null : before.GetValueOrDefault(r.Text("eventName")),
            r.Long("totalUsers"), r.Long("keyEvents") > 0)).ToList();
    }

    /// <summary>A report's rows, their columns found by name.</summary>
    private sealed class Table
    {
        private readonly Dictionary<string, int> _dimensions;
        private readonly Dictionary<string, int> _metrics;
        private readonly IList<Row> _rows;

        public Table(RunReportResponse report)
            : this(report.DimensionHeaders?.Select(h => h.Name), report.MetricHeaders?.Select(h => h.Name), report.Rows)
        {
        }

        public Table(RunRealtimeReportResponse report)
            : this(report.DimensionHeaders?.Select(h => h.Name), report.MetricHeaders?.Select(h => h.Name), report.Rows)
        {
        }

        private Table(IEnumerable<string>? dimensions, IEnumerable<string>? metrics, IList<Row>? rows)
        {
            _dimensions = Positions(dimensions);
            _metrics = Positions(metrics);
            _rows = rows ?? [];
        }

        /// <summary>The rows of one date range; every row when the report has a single range.</summary>
        public IEnumerable<TableRow> Rows(string? range = null)
            => _rows
                .Where(r => range is null || !_dimensions.TryGetValue(DateRangeColumn, out var i) || r.DimensionValues[i].Value == range)
                .Select(r => new TableRow(this, r));

        public string Text(Row row, string name) => row.DimensionValues[_dimensions[name]].Value ?? string.Empty;

        public string Metric(Row row, string name) => row.MetricValues[_metrics[name]].Value ?? string.Empty;

        private static Dictionary<string, int> Positions(IEnumerable<string>? names)
            => (names ?? []).Select((name, index) => (name, index)).ToDictionary(c => c.name, c => c.index);
    }

    private readonly record struct TableRow(Table Table, Row Row)
    {
        public string Text(string name) => Table.Text(Row, name);

        public long Long(string name) => (long)Math.Round(Double(name));

        public double Double(string name)
            => double.TryParse(Table.Metric(Row, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
