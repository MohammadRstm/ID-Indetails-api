using ID_Indetails.LoadTests.Tool.Report;
using ID_Indetails.LoadTests.Tool.Verification;
using Shouldly;

namespace ID_Indetails.LoadTests.Tool.Tests;

public class ReportTests
{
    [Fact]
    public void Reads_the_scripts_lines_raw_or_as_logfmt()
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path,
        [
            """LEDGER {"run":"r1","kind":"work_entry.created","t":1,"id":7,"user":3}""",
            """time="2026-10-05T01:00:00Z" level=info msg="ERROR {\"t\":2,\"stage\":\"50 users\",\"name\":\"GET /api/x\",\"status\":500,\"code\":\"internal_error\"}" source=console""",
            """AFTERWARDS {"race":"payments_same_invoice","rule":"the invoice is never overpaid","holds":true}""",
            "UNLISTED GET /api/new",
            "something else entirely",
        ]);

        var log = RunLog.Read(path);

        log.Ledger.ShouldHaveSingleItem().GetProperty("id").GetInt64().ShouldBe(7);
        log.Errors.ShouldHaveSingleItem().GetProperty("status").GetInt32().ShouldBe(500);
        log.Afterwards.ShouldHaveSingleItem().GetProperty("holds").GetBoolean().ShouldBeTrue();
        log.Unlisted.ShouldBe(["GET /api/new"]);
        File.Delete(path);
    }

    [Theory]
    [InlineData("/api/projects/123/labour-weeks/2026-10-05?x=1", "/api/projects/:id/labour-weeks/:date")]
    [InlineData("/api/me/projects", "/api/me/projects")]
    [InlineData("/api/finance/design/summary?from=2026-01-01", "/api/finance/design/summary")]
    public void Names_routes_as_the_k6_scripts_do(string path, string route)
    {
        ServerLogs.Route(path).ShouldBe(route);
    }

    [Theory]
    [InlineData("MySqlConnector.MySqlException (0x80004005): Deadlock found when trying to get lock", "deadlock")]
    [InlineData("MySqlConnector.MySqlException: Lock wait timeout exceeded; try restarting transaction", "lock wait timeout")]
    [InlineData("MySqlConnector.MySqlException: error connecting: Timeout expired. All pooled connections are in use.", "connection pool exhausted")]
    [InlineData("Microsoft.EntityFrameworkCore.DbUpdateException: ... Duplicate entry 'x' for key", "duplicate key")]
    public void Names_database_failures_without_their_messages(string exception, string kind)
    {
        var named = ServerLogs.ExceptionKind(exception);

        named.ShouldContain(kind);
        named.ShouldNotContain("'x'");
    }

    [Fact]
    public void Reads_k6_numbers_per_step_and_endpoint()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "finishedAt": 1791162000000,
              "run": "r1",
              "state": { "testRunDurationMs": 600000 },
              "loadProfile": { "name": "stages", "steps": [ { "users": 50, "ramp": "1m", "hold": "4m" }, { "users": 70, "ramp": "1m", "hold": "4m" } ] },
              "metrics": {
                "http_reqs": { "type": "counter", "values": { "count": 1000, "rate": 1.6 } },
                "http_req_duration": { "type": "trend", "values": { "count": 1000, "med": 40, "p(95)": 120, "p(99)": 300, "max": 900 } },
                "http_req_failed": { "type": "rate", "values": { "rate": 0.01 } },
                "http_reqs{stage:50 users}": { "type": "counter", "values": { "count": 480 } },
                "http_req_duration{stage:50 users}": { "type": "trend", "values": { "count": 480, "med": 35, "p(95)": 100, "p(99)": 200, "max": 400 },
                  "thresholds": { "max>=0": { "ok": true } } },
                "http_req_duration{name:GET /api/projects/:id}": { "type": "trend", "values": { "count": 90, "med": 50, "p(95)": 210, "p(99)": 400, "max": 500 } },
                "http_req_duration{name:GET /api/projects/:id,stage:70 users}": { "type": "trend", "values": { "count": 50, "med": 60, "p(95)": 250, "p(99)": 400, "max": 500 } },
                "http_req_failed{name:GET /api/projects/:id}": { "type": "rate", "values": { "rate": 0.02 } },
                "checks": { "type": "rate", "values": { "rate": 0.99 },
                  "thresholds": { "rate>0.995": { "ok": false } } }
              }
            }
            """);

        var run = K6Summary.Read(path);

        run.Steps.Select(s => s.Label).ShouldBe(["ramp to 50", "50 users", "ramp to 70", "70 users", "ramp down"]);
        run.Requests.ShouldBe(1000);
        run.StepNumbers.Single(s => s.Step.Label == "50 users").PerSecond.ShouldBe(2.0);
        run.Endpoints.ShouldHaveSingleItem().FailedRate.ShouldBe(0.02);
        run.P95ByStep["GET /api/projects/:id"]["70 users"].ShouldBe(250);
        run.FailedThresholds.ShouldHaveSingleItem().ShouldContain("checks");
        run.StepAt(run.StartedUtc.AddMinutes(2)).ShouldBe("50 users");
        File.Delete(path);
    }
}
