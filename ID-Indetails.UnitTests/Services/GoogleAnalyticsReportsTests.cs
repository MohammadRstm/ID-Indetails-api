using System.Text;
using Google.Apis.AnalyticsData.v1beta.Data;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Services.GoogleAnalytics.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class GoogleAnalyticsReportsTests
{
    private static readonly AnalyticsPeriod Compared =
        new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 30));

    private static RunReportResponse Report(string[] dimensions, string[] metrics, params (string[] Dimensions, string[] Metrics)[] rows) => new()
    {
        DimensionHeaders = dimensions.Select(n => new DimensionHeader { Name = n }).ToList(),
        MetricHeaders = metrics.Select(n => new MetricHeader { Name = n }).ToList(),
        Rows = rows.Select(r => new Row
        {
            DimensionValues = r.Dimensions.Select(v => new DimensionValue { Value = v }).ToList(),
            MetricValues = r.Metrics.Select(v => new MetricValue { Value = v }).ToList(),
        }).ToList(),
    };

    private static RunReportResponse Empty() => new();

    [Fact]
    public void Each_batch_holds_at_most_five_reports_and_only_totals_days_and_events_are_compared()
    {
        var traffic = GoogleAnalyticsReports.Traffic(Compared).Requests;
        var audience = GoogleAnalyticsReports.Audience(Compared).Requests;

        traffic.Count.ShouldBeLessThanOrEqualTo(5);
        audience.Count.ShouldBeLessThanOrEqualTo(5);
        traffic.Select(r => r.DateRanges.Count).ShouldBe([2, 2, 1, 1, 1]);
        audience.Select(r => r.DateRanges.Count).ShouldBe([1, 1, 1, 2]);
        traffic[0].DateRanges.Select(r => (r.StartDate, r.EndDate, r.Name)).ShouldBe([("2026-10-01", "2026-10-07", "current"), ("2026-09-24", "2026-09-30", "previous")]);

        GoogleAnalyticsReports.Traffic(Compared with { PreviousFrom = null, PreviousTo = null }).Requests.ShouldAllBe(r => r.DateRanges.Count == 1);
    }

    [Fact]
    public void Answers_are_read_by_column_name_and_split_by_date_range()
    {
        // Google may order its columns as it likes: the metrics here are not in the order asked
        var totals = Report(
            ["dateRange"],
            ["sessions", "activeUsers", "newUsers", "engagedSessions", "screenPageViews", "userEngagementDuration", "eventCount", "keyEvents"],
            (["current"], ["50", "40", "30", "20", "150", "2400.5", "900", "6"]),
            (["previous"], ["25", "20", "15", "10", "75", "1200", "450", "3"]));
        totals.Metadata = new ResponseMetaData { TimeZone = "Asia/Beirut" };
        var days = Report(["date", "dateRange"], ["activeUsers", "sessions", "screenPageViews"],
            (["20261002", "current"], ["5", "6", "9"]), (["20260925", "previous"], ["1", "1", "2"]));
        var channels = Report(["sessionDefaultChannelGroup"], ["sessions", "activeUsers", "engagedSessions", "keyEvents"], (["Organic Search"], ["40", "30", "10", "2"]));
        var sources = Report(["sessionSource", "sessionMedium", "sessionCampaignName"], ["sessions", "activeUsers", "engagedSessions", "keyEvents"],
            (["google", "organic", "(organic)"], ["40", "30", "20", "2"]));
        var pages = Report(["pagePath", "pageTitle"], ["screenPageViews", "activeUsers", "userEngagementDuration", "eventCount"],
            (["/projects/villa-rizk", "Villa Rizk · ID-Indetails"], ["30", "10", "600", "80"]));
        var countries = Report(["countryId", "country"], ["activeUsers", "sessions"], (["LB", "Lebanon"], ["30", "35"]));
        var devices = Report(["deviceCategory"], ["activeUsers", "sessions"], (["mobile"], ["25", "30"]));
        var visitors = Report(["newVsReturning"], ["activeUsers", "sessions"], (["new"], ["30", "32"]), (["returning"], ["10", "18"]));
        var events = Report(["eventName", "dateRange"], ["eventCount", "totalUsers", "keyEvents"],
            (["generate_lead", "current"], ["3", "3", "3"]), (["page_view", "current"], ["150", "40", "0"]), (["generate_lead", "previous"], ["1", "1", "1"]));
        events.Metadata = new ResponseMetaData { SubjectToThresholding = true };

        var report = GoogleAnalyticsReports.Read(
            Compared,
            new BatchRunReportsResponse { Reports = [totals, days, channels, sources, pages] },
            new BatchRunReportsResponse { Reports = [countries, devices, visitors, events] });

        report.Totals.ShouldBe(new AnalyticsCounts(40, 30, 50, 20, 150, 2400.5, 900, 6));
        report.PreviousTotals!.Sessions.ShouldBe(25);
        report.Days.Single().ShouldBe(new AnalyticsDayCounts(new DateOnly(2026, 10, 2), 5, 6, 9));
        report.PreviousDays.Single().Date.ShouldBe(new DateOnly(2026, 9, 25));
        report.Channels.Single().EngagementRate.ShouldBe(0.25);
        report.Sources.Single().Campaign.ShouldBe("(organic)");
        report.Pages.Single().AverageEngagementSeconds.ShouldBe(60);
        report.Pages.Single().ProjectTitle.ShouldBeNull();
        report.Countries.Single().Code.ShouldBe("LB");
        report.VisitorTypes.Select(v => v.Key).ShouldBe(["new", "returning"]);
        report.Events.Select(e => (e.Name, e.Count, e.PreviousCount, e.IsKeyEvent)).ShouldBe([("generate_lead", 3L, (long?)1, true), ("page_view", 150L, (long?)0, false)]);
        report.Thresholded.ShouldBeTrue();
        report.TimeZone.ShouldBe("Asia/Beirut");
    }

    [Fact]
    public void A_property_with_nothing_recorded_reads_as_zero()
    {
        var report = GoogleAnalyticsReports.Read(
            Compared,
            new BatchRunReportsResponse { Reports = [Empty(), Empty(), Empty(), Empty(), Empty()] },
            new BatchRunReportsResponse { Reports = [Empty(), Empty(), Empty(), Empty()] });

        report.Totals.Sessions.ShouldBe(0);
        report.PreviousTotals.ShouldNotBeNull().Visitors.ShouldBe(0);
        report.Days.ShouldBeEmpty();
        report.Events.ShouldBeEmpty();
        report.Thresholded.ShouldBeFalse();
    }

    [Fact]
    public void Realtime_minutes_come_oldest_first()
    {
        RunRealtimeReportResponse Realtime(string[] dimensions, string[] metrics, params (string[] D, string[] M)[] rows)
        {
            var r = Report(dimensions, metrics, rows);
            return new RunRealtimeReportResponse { DimensionHeaders = r.DimensionHeaders, MetricHeaders = r.MetricHeaders, Rows = r.Rows };
        }

        var now = GoogleAnalyticsReports.ReadRealtime(
        [
            Realtime(["minutesAgo"], ["activeUsers"], (["00"], ["2"]), (["29"], ["1"]), (["45"], ["9"])),
            Realtime([], ["activeUsers"], ([], ["3"])),
            Realtime(["unifiedScreenName"], ["screenPageViews", "activeUsers"], (["Contact · ID-Indetails"], ["4", "2"])),
            new RunRealtimeReportResponse(),
        ]);

        now.VisitorsNow.ShouldBe(3);
        now.VisitorsPerMinute.Count.ShouldBe(30);
        now.VisitorsPerMinute[29].ShouldBe(2);
        now.VisitorsPerMinute[0].ShouldBe(1);
        now.VisitorsPerMinute.Sum().ShouldBe(3);
        now.Pages.Single().Title.ShouldBe("Contact · ID-Indetails");
        now.Countries.ShouldBeEmpty();
    }

    [Fact]
    public void Only_a_service_account_s_json_key_is_taken()
    {
        static string Base64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        const string key = """{"type":"service_account","client_email":"ga@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n"}""";

        GoogleAnalyticsClient.KeyJson(Base64(key)).ShouldBe(key);
        GoogleAnalyticsClient.KeyJson(Base64("""{"type":"authorized_user","client_email":"a","private_key":"b"}""")).ShouldBeNull();
        GoogleAnalyticsClient.KeyJson(Base64("""{"type":"service_account","client_email":"a"}""")).ShouldBeNull();
        GoogleAnalyticsClient.KeyJson("not base64!").ShouldBeNull();
        GoogleAnalyticsClient.KeyJson(Base64("not json")).ShouldBeNull();
    }
}
