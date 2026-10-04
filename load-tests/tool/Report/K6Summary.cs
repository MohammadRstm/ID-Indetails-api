using System.Text.Json;
using System.Text.RegularExpressions;

namespace ID_Indetails.LoadTests.Tool.Report;

public sealed record Step(string Label, int Users, DateTime Start, DateTime End)
{
    public double Seconds => (End - Start).TotalSeconds;

    public bool IsHold => Label.EndsWith(" users", StringComparison.Ordinal);
}

public sealed record Timing(long Count, double P50, double P95, double P99, double Max);

public sealed record StepNumbers(Step Step, long Requests, double PerSecond, Timing? Timing, double FailedRate, long Unexpected);

public sealed record EndpointNumbers(string Name, Timing Timing, double FailedRate);

/// <summary>
/// The numbers k6 kept (main.js handleSummary → k6-summary.json): per load step, per endpoint, and per endpoint in
/// each steady step, with the profile and the clock times needed to place them.
/// </summary>
public sealed partial class K6Summary
{
    public string Profile { get; private init; } = string.Empty;

    public string Run { get; private init; } = string.Empty;

    public DateTime StartedUtc { get; private init; }

    public DateTime FinishedUtc { get; private init; }

    public List<Step> Steps { get; } = [];

    public List<StepNumbers> StepNumbers { get; } = [];

    public List<EndpointNumbers> Endpoints { get; } = [];

    /// <summary>Endpoint → steady step → 95th percentile, in milliseconds.</summary>
    public Dictionary<string, Dictionary<string, double>> P95ByStep { get; } = [];

    public List<string> FailedThresholds { get; } = [];

    public Timing? Overall { get; private set; }

    public long Requests { get; private set; }

    public double FailedRate { get; private set; }

    public double ChecksRate { get; private set; } = 1;

    public Dictionary<string, double> Counters { get; } = [];

    public static K6Summary Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var finished = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("finishedAt").GetInt64()).UtcDateTime;
        var duration = root.GetProperty("state").GetProperty("testRunDurationMs").GetDouble();
        var summary = new K6Summary
        {
            Profile = root.GetProperty("loadProfile").GetProperty("name").GetString()!,
            Run = root.TryGetProperty("run", out var run) ? run.GetString()! : string.Empty,
            FinishedUtc = finished,
            StartedUtc = finished.AddMilliseconds(-duration),
        };
        summary.ReadSteps(root.GetProperty("loadProfile"));

        var metrics = new Dictionary<(string Metric, string Name, string Stage), JsonElement>();
        foreach (var metric in root.GetProperty("metrics").EnumerateObject())
        {
            var (name, tags) = Parse(metric.Name);
            metrics[(name, tags.GetValueOrDefault("name", string.Empty), tags.GetValueOrDefault("stage", string.Empty))] = metric.Value;
            if (metric.Value.TryGetProperty("thresholds", out var thresholds))
            {
                foreach (var threshold in thresholds.EnumerateObject().Where(t => t.Value.TryGetProperty("ok", out var ok) && !ok.GetBoolean()))
                {
                    summary.FailedThresholds.Add($"{metric.Name}: {threshold.Name}");
                }
            }

            if (tags.Count == 0 && metric.Value.GetProperty("type").GetString() == "counter")
            {
                summary.Counters[name] = Value(metric.Value, "count");
            }
        }

        summary.Overall = TimingOf(metrics.GetValueOrDefault(("http_req_duration", "", "")));
        summary.Requests = (long)Value(metrics.GetValueOrDefault(("http_reqs", "", "")), "count");
        summary.FailedRate = Value(metrics.GetValueOrDefault(("http_req_failed", "", "")), "rate");
        if (metrics.TryGetValue(("checks", "", ""), out var checks))
        {
            summary.ChecksRate = Value(checks, "rate");
        }

        foreach (var step in summary.Steps)
        {
            var requests = (long)Value(metrics.GetValueOrDefault(("http_reqs", "", step.Label)), "count");
            summary.StepNumbers.Add(new StepNumbers(step, requests, step.Seconds > 0 ? requests / step.Seconds : 0,
                TimingOf(metrics.GetValueOrDefault(("http_req_duration", "", step.Label))),
                Value(metrics.GetValueOrDefault(("http_req_failed", "", step.Label)), "rate"),
                (long)Value(metrics.GetValueOrDefault(("unexpected_responses", "", step.Label)), "count")));
        }

        foreach (var ((metric, name, stage), value) in metrics.Where(m => m.Key.Name.Length > 0))
        {
            if (metric == "http_req_duration" && stage.Length == 0 && TimingOf(value) is { Count: > 0 } timing)
            {
                summary.Endpoints.Add(new EndpointNumbers(name, timing, Value(metrics.GetValueOrDefault(("http_req_failed", name, "")), "rate")));
            }
            else if (metric == "http_req_duration" && stage.Length > 0 && TimingOf(value) is { Count: > 0 } byStep)
            {
                summary.P95ByStep.TryAdd(name, []);
                summary.P95ByStep[name][stage] = byStep.P95;
            }
        }

        return summary;
    }

    /// <summary>The same periods and labels as the k6 scripts' profile.js.</summary>
    private void ReadSteps(JsonElement profile)
    {
        var at = StartedUtc;
        foreach (var step in profile.GetProperty("steps").EnumerateArray())
        {
            var users = step.GetProperty("users").GetInt32();
            var ramp = Duration(step.GetProperty("ramp").GetString()!);
            var hold = Duration(step.GetProperty("hold").GetString()!);
            Steps.Add(new Step($"ramp to {users}", users, at, at + ramp));
            Steps.Add(new Step($"{users} users", users, at + ramp, at + ramp + hold));
            at += ramp + hold;
        }

        Steps.Add(new Step("ramp down", 0, at, FinishedUtc > at ? FinishedUtc : at));
    }

    public string? StepAt(DateTime utc) => Steps.FirstOrDefault(s => utc >= s.Start && utc < s.End)?.Label;

    private static TimeSpan Duration(string text)
    {
        var match = DurationPattern().Match(text);
        var number = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value switch { "s" => TimeSpan.FromSeconds(number), "m" => TimeSpan.FromMinutes(number), _ => TimeSpan.FromHours(number) };
    }

    private static (string Name, Dictionary<string, string> Tags) Parse(string key)
    {
        var brace = key.IndexOf('{');
        if (brace < 0)
        {
            return (key, []);
        }

        var tags = key[(brace + 1)..^1].Split(',')
            .Select(pair => pair.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);
        return (key[..brace], tags);
    }

    private static Timing? TimingOf(JsonElement metric)
        => metric.ValueKind == JsonValueKind.Object
            ? new Timing((long)Value(metric, "count"), Value(metric, "med"), Value(metric, "p(95)"), Value(metric, "p(99)"), Value(metric, "max"))
            : null;

    private static double Value(JsonElement metric, string name)
        => metric.ValueKind == JsonValueKind.Object && metric.TryGetProperty("values", out var values) && values.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    [GeneratedRegex(@"^(\d+)(s|m|h)$")]
    private static partial Regex DurationPattern();
}
