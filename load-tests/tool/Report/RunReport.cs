using System.Globalization;
using System.Text;
using System.Text.Json;
using ID_Indetails.LoadTests.Tool.Verification;

namespace ID_Indetails.LoadTests.Tool.Report;

/// <summary>
/// The report of one run, from what the runner put in its folder: k6's numbers (k6-summary.json), the scripts'
/// console (k6-console.log), and from the server the API's and nginx's logs, the samples, the Mailpit count and the
/// chaos log (collected/), and the database verification (verification.json). Writes report.md and report.json.
/// </summary>
public static class RunReport
{
    public static async Task<string> WriteAsync(string folder)
    {
        var run = K6Summary.Read(Path.Combine(folder, "k6-summary.json"));
        var console = File.Exists(Path.Combine(folder, "k6-console.log")) ? RunLog.Read(Path.Combine(folder, "k6-console.log")) : new RunLog();
        var collected = Path.Combine(folder, "collected");
        var api = File.Exists(Path.Combine(collected, "lt-api.log")) ? ServerLogs.ReadApi(Path.Combine(collected, "lt-api.log"), run) : [];
        var nginx = File.Exists(Path.Combine(collected, "lt-web.log")) ? ServerLogs.ReadNginx(Path.Combine(collected, "lt-web.log"), run) : null;
        var samples = Directory.Exists(Path.Combine(collected, "monitor")) ? Samples.Read(Path.Combine(collected, "monitor"), run) : [];
        var mail = File.Exists(Path.Combine(collected, "mail-count.txt")) ? File.ReadAllText(Path.Combine(collected, "mail-count.txt")).Trim() : "not collected";
        var verification = File.Exists(Path.Combine(folder, "verification.json"))
            ? JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "verification.json"))).RootElement
            : (JsonElement?)null;
        var chaos = File.Exists(Path.Combine(collected, "chaos.log")) ? await File.ReadAllLinesAsync(Path.Combine(collected, "chaos.log")) : [];

        var md = new StringBuilder();
        Headline(md, run, verification, mail);
        Steps(md, run, samples, console);
        Endpoints(md, run);
        Errors(md, run, console);
        ServerSide(md, api, nginx);
        Server(md, samples, collected);
        Integrity(md, verification);
        Recovery(md, chaos, console);

        await File.WriteAllTextAsync(Path.Combine(folder, "report.md"), md.ToString());
        await File.WriteAllTextAsync(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new
        {
            profile = run.Profile,
            run = run.Run,
            started = run.StartedUtc,
            finished = run.FinishedUtc,
            requests = run.Requests,
            failedRate = run.FailedRate,
            overall = run.Overall,
            failedThresholds = run.FailedThresholds,
            steps = run.StepNumbers.Select(s => new { step = s.Step.Label, users = s.Step.Users, s.Requests, perSecond = s.PerSecond, timing = s.Timing, s.FailedRate, s.Unexpected }),
            endpoints = run.Endpoints.OrderByDescending(e => e.Timing.P95).Select(e => new { e.Name, e.Timing, e.FailedRate }),
            errors = console.Errors.GroupBy(e => $"{Text(e, "name")} {Number(e, "status")} {Text(e, "code")}").ToDictionary(g => g.Key, g => g.Count()),
            server = api.Select(g => new { g.Kind, g.What, g.Count, g.PerStep }),
            samples = samples.Select(m => new { m.What, m.Unit, m.Peak, m.Average, m.PeakPerStep }),
            emailsCaught = mail,
            degradation = Degradation(run),
            databasePassed = verification?.GetProperty("passed").GetBoolean(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        return md.ToString();
    }

    private static void Headline(StringBuilder md, K6Summary run, JsonElement? verification, string mail)
    {
        md.AppendLine($"# Load test: {run.Profile}, run {run.Run}").AppendLine();
        md.AppendLine($"{run.StartedUtc:yyyy-MM-dd HH:mm} to {run.FinishedUtc:HH:mm} UTC ({(run.FinishedUtc - run.StartedUtc).TotalMinutes:0} minutes).").AppendLine();
        md.AppendLine($"- **Requests:** {run.Requests:N0}, failed {Percent(run.FailedRate)}; checks passed {Percent(run.ChecksRate)}");
        if (run.Overall is { } overall)
        {
            md.AppendLine($"- **Response times:** p50 {Ms(overall.P50)}, p95 {Ms(overall.P95)}, p99 {Ms(overall.P99)}, max {Ms(overall.Max)}");
        }

        md.AppendLine($"- **Thresholds:** {(run.FailedThresholds.Count == 0 ? "all passed" : string.Join("; ", run.FailedThresholds.Select(t => $"FAILED {t}")))}");
        md.AppendLine($"- **Database verification:** {(verification is { } v ? v.GetProperty("passed").GetBoolean() ? "passed" : "**FAILED** (see below)" : "not run")}");
        md.AppendLine($"- **Emails caught by Mailpit:** {mail} (a normal test sends none)");
        md.AppendLine($"- **Where it degrades:** {Degradation(run)}").AppendLine();
    }

    /// <summary>The first steady step where the 95th percentile doubles from the first one's, passes 1.5 s, or errors pass 1%.</summary>
    private static string Degradation(K6Summary run)
    {
        var holds = run.StepNumbers.Where(s => s.Step.IsHold && s.Timing is { Count: > 0 }).ToList();
        if (holds.Count == 0)
        {
            return "no steady step to judge";
        }

        var baseline = holds[0].Timing!.P95;
        var first = holds.FirstOrDefault(s => s.Timing!.P95 > Math.Max(2 * baseline, 1500) || s.FailedRate > 0.01);
        if (first is null)
        {
            return $"no significant degradation up to {holds[^1].Step.Users} users (p95 {Ms(holds[^1].Timing!.P95)}, errors {Percent(holds[^1].FailedRate)})";
        }

        return first == holds[0]
            ? $"already at the first steady step ({first.Step.Users} users): p95 {Ms(first.Timing!.P95)}, errors {Percent(first.FailedRate)}; look at the errors before the load"
            : $"from about **{first.Step.Users} users**: p95 {Ms(first.Timing!.P95)} against {Ms(baseline)} at {holds[0].Step.Users}, errors {Percent(first.FailedRate)}";
    }

    private static void Steps(StringBuilder md, K6Summary run, List<Measure> samples, RunLog console)
    {
        string Peak(string what, string step) => samples.FirstOrDefault(m => m.What == what)?.PeakPerStep.GetValueOrDefault(step) is { } v and > 0 ? $"{v:0}" : "";
        md.AppendLine("## Per load step").AppendLine();
        md.AppendLine("| Step | Requests | Req/s | p50 | p95 | p99 | Failed | 4xx | 5xx | Timeouts | CPU % | Memory MB | DB connections |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var s in run.StepNumbers.Where(s => s.Requests > 0))
        {
            var errors = console.Errors.Where(e => Text(e, "stage") == s.Step.Label).ToList();
            md.AppendLine($"| {s.Step.Label} | {s.Requests:N0} | {s.PerSecond:0.0} | {Ms(s.Timing?.P50)} | {Ms(s.Timing?.P95)} | {Ms(s.Timing?.P99)} | {Percent(s.FailedRate)} | " +
                $"{errors.Count(e => Number(e, "status") is >= 400 and < 500)} | {errors.Count(e => Number(e, "status") >= 500)} | {errors.Count(e => Number(e, "status") == 0)} | " +
                $"{Peak("Server CPU", s.Step.Label)} | {Peak("Memory used", s.Step.Label)} | {Peak("Database connections", s.Step.Label)} |");
        }

        md.AppendLine();
    }

    private static void Endpoints(StringBuilder md, K6Summary run)
    {
        md.AppendLine("## Slowest endpoints (95th percentile)").AppendLine();
        md.AppendLine("| Endpoint | Requests | p50 | p95 | p99 | Max | Failed |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var e in run.Endpoints.OrderByDescending(e => e.Timing.P95).Take(15))
        {
            md.AppendLine($"| `{e.Name}` | {e.Timing.Count:N0} | {Ms(e.Timing.P50)} | {Ms(e.Timing.P95)} | {Ms(e.Timing.P99)} | {Ms(e.Timing.Max)} | {Percent(e.FailedRate)} |");
        }

        var holds = run.Steps.Where(s => s.IsHold).Select(s => s.Label).ToList();
        if (holds.Count > 1)
        {
            md.AppendLine().AppendLine("### How the slowest endpoints change as users grow (p95)").AppendLine();
            md.AppendLine($"| Endpoint | {string.Join(" | ", holds)} |").AppendLine($"|---|{string.Concat(holds.Select(_ => "---|"))}");
            foreach (var (name, byStep) in run.P95ByStep.OrderByDescending(p => p.Value.Values.DefaultIfEmpty(0).Max()).Take(10))
            {
                md.AppendLine($"| `{name}` | {string.Join(" | ", holds.Select(h => byStep.TryGetValue(h, out var v) ? Ms(v) : ""))} |");
            }
        }

        md.AppendLine();
    }

    private static void Errors(StringBuilder md, K6Summary run, RunLog console)
    {
        md.AppendLine("## Errors seen by the users (k6)").AppendLine();
        if (console.Errors.Count == 0)
        {
            md.AppendLine("Every answer was the expected one.").AppendLine();
            return;
        }

        var kinds = new (string Kind, Func<double, bool> Is)[]
        {
            ("timeouts and network errors", s => s == 0), ("authentication (401)", s => s == 401), ("authorization (403)", s => s == 403),
            ("not found (404)", s => s == 404), ("conflicts (409)", s => s == 409), ("business rules (422)", s => s == 422),
            ("validation (400)", s => s == 400), ("rate limited (429)", s => s == 429), ("server errors (5xx)", s => s >= 500),
        };
        md.AppendLine("| Kind | Count |").AppendLine("|---|---|");
        foreach (var (kind, isIt) in kinds)
        {
            var count = console.Errors.Count(e => isIt(Number(e, "status")));
            if (count > 0)
            {
                md.AppendLine($"| {kind} | {count:N0} |");
            }
        }

        md.AppendLine().AppendLine("| Endpoint | Status | Error code | Count | Steps |").AppendLine("|---|---|---|---|---|");
        foreach (var group in console.Errors.GroupBy(e => (Text(e, "name"), Number(e, "status"), Text(e, "code"))).OrderByDescending(g => g.Count()).Take(25))
        {
            var steps = string.Join(", ", group.GroupBy(e => Text(e, "stage")).Select(s => $"{s.Key}: {s.Count()}"));
            md.AppendLine($"| `{group.Key.Item1}` | {group.Key.Item2} | {group.Key.Item3} | {group.Count():N0} | {steps} |");
        }

        var traces = console.Errors.Where(e => Number(e, "status") >= 500).Select(e => Text(e, "traceId")).Where(t => t.Length > 0).Distinct().Take(10).ToList();
        if (traces.Count > 0)
        {
            md.AppendLine().AppendLine($"Trace ids of server errors, to find in the API log: {string.Join(", ", traces.Select(t => $"`{t}`"))}");
        }

        if (console.Unlisted.Count > 0)
        {
            md.AppendLine().AppendLine($"Endpoints not broken down per step (add them to lib/endpoints.js): {string.Join("; ", console.Unlisted.Distinct())}");
        }

        md.AppendLine();
    }

    private static void ServerSide(StringBuilder md, List<LogGroup> api, ServerLogs.NginxResult? nginx)
    {
        md.AppendLine("## What the server logged").AppendLine();
        if (api.Count == 0)
        {
            md.AppendLine("No API log was collected, or it held no warnings, errors or refusals.").AppendLine();
        }
        else
        {
            md.AppendLine("Exceptions, warnings and errors by kind, and the requests the API refused (by error code). Message values are never copied here.").AppendLine();
            md.AppendLine("| Kind | What | Count | Steps |").AppendLine("|---|---|---|---|");
            foreach (var g in api.Take(30))
            {
                md.AppendLine($"| {g.Kind} | {g.What.Replace("|", "/")} | {g.Count:N0} | {string.Join(", ", g.PerStep.Select(s => $"{s.Key}: {s.Value}"))} |");
            }

            md.AppendLine();
        }

        if (nginx is null)
        {
            return;
        }

        md.AppendLine($"nginx answered {string.Join(", ", nginx.Statuses.Select(s => $"{s.Key}: {s.Value:N0}"))}.");
        if (nginx.ServerErrorsPerStep.Count > 0)
        {
            md.AppendLine($"Server errors (5xx, including 502/504 when the API was down or slow) per step: {string.Join(", ", nginx.ServerErrorsPerStep.Select(s => $"{s.Key}: {s.Value}"))}.");
        }

        md.AppendLine().AppendLine("The API's own answer times, measured by nginx in front of it (without the network and TLS):").AppendLine();
        md.AppendLine("| Endpoint | Requests | 5xx | p50 | p95 |").AppendLine("|---|---|---|---|---|");
        foreach (var e in nginx.Endpoints.Take(10))
        {
            md.AppendLine($"| `{e.Name}` | {e.Count:N0} | {e.ServerErrors} | {Ms(e.P50Ms)} | {Ms(e.P95Ms)} |");
        }

        md.AppendLine();
    }

    private static void Server(StringBuilder md, List<Measure> samples, string collected)
    {
        md.AppendLine("## The server").AppendLine();
        if (samples.Count == 0)
        {
            md.AppendLine("No samples were collected.").AppendLine();
            return;
        }

        md.AppendLine("| Measure | Peak | Average |").AppendLine("|---|---|---|");
        foreach (var m in samples)
        {
            md.AppendLine($"| {m.What} | {m.Peak:0.#} {m.Unit} | {m.Average:0.#} {m.Unit} |");
        }

        var statements = Path.Combine(collected, "monitor", "statements.tsv");
        if (File.Exists(statements) && File.ReadAllLines(statements) is { Length: > 0 } rows)
        {
            md.AppendLine().AppendLine("### Database statements that took the most time").AppendLine();
            md.AppendLine("| Statement (shape) | Calls | Total ms | Avg ms | Max ms | Rows examined | No index |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var cells in rows.Take(10).Select(r => r.Split('\t')).Where(c => c.Length >= 8))
            {
                var shape = cells[0].Length > 140 ? cells[0][..140] + "…" : cells[0];
                md.AppendLine($"| `{shape.Replace("|", "/").Replace("`", "'")}` | {cells[1]} | {cells[2]} | {cells[3]} | {cells[4]} | {cells[5]} | {cells[7]} |");
            }
        }

        var deadlock = Path.Combine(collected, "monitor", "deadlock.txt");
        if (File.Exists(deadlock) && new FileInfo(deadlock).Length > 0)
        {
            md.AppendLine().AppendLine("**MySQL recorded a deadlock** during the run; the details are in collected/monitor/deadlock.txt.");
        }

        md.AppendLine();
    }

    private static void Integrity(StringBuilder md, JsonElement? verification)
    {
        md.AppendLine("## Data integrity").AppendLine();
        if (verification is not { } v)
        {
            md.AppendLine("The database wasn't verified.").AppendLine();
            return;
        }

        var broken = v.GetProperty("rules").EnumerateArray().Where(r => r.GetProperty("violations").GetInt64() > 0).ToList();
        var mismatched = v.GetProperty("ledger").EnumerateArray().Where(e => !e.GetProperty("holds").GetBoolean()).ToList();
        var races = v.GetProperty("races").EnumerateArray().Where(r => !r.GetProperty("holds").GetBoolean()).ToList();
        md.AppendLine(v.GetProperty("passed").GetBoolean()
            ? $"Every data rule holds, and the database holds exactly what the run did ({v.GetProperty("ledger").GetArrayLength()} checks). Details: verification.md."
            : "**Problems found** (details in verification.md):");
        foreach (var r in broken)
        {
            md.AppendLine($"- Rule broken: {r.GetProperty("rule").GetString()} ({r.GetProperty("violations").GetInt64()} rows)");
        }

        foreach (var e in mismatched)
        {
            md.AppendLine($"- {e.GetProperty("check").GetString()}: expected {e.GetProperty("expected").GetInt64()}, found {e.GetProperty("actual").GetInt64()}");
        }

        foreach (var r in races)
        {
            md.AppendLine($"- Race {r.GetProperty("race").GetString()}: {r.GetProperty("rule").GetString()} does not hold");
        }

        md.AppendLine();
    }

    /// <summary>For each chaos action: how many requests failed in the two minutes after it, and when the last one did.</summary>
    private static void Recovery(StringBuilder md, string[] chaos, RunLog console)
    {
        var actions = chaos.Select(l => l.Split(' ', 4)).Where(p => p.Length >= 3 && p[1] == "chaos")
            .Select(p => (At: DateTime.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), What: string.Join(' ', p.Skip(2))))
            .ToList();
        if (actions.Count == 0)
        {
            return;
        }

        md.AppendLine("## Recovery").AppendLine();
        md.AppendLine("| When (UTC) | Action | Failed requests in the next 2 min | Last failure after |").AppendLine("|---|---|---|---|");
        foreach (var (at, what) in actions)
        {
            var failures = console.Errors.Select(e => DateTimeOffset.FromUnixTimeMilliseconds((long)Number(e, "t")).UtcDateTime)
                .Where(t => t >= at.AddSeconds(-60) && t < at.AddMinutes(2)).ToList();
            md.AppendLine($"| {at:HH:mm:ss} | {what} | {failures.Count} | {(failures.Count == 0 ? "-" : $"{(failures.Max() - at).TotalSeconds:0} s")} |");
        }

        md.AppendLine();
    }

    private static string Ms(double? value) => value is { } v ? v >= 1000 ? $"{v / 1000:0.00} s" : $"{v:0} ms" : "";

    private static string Percent(double rate) => $"{rate * 100:0.##}%";

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

    private static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : -1;
}
