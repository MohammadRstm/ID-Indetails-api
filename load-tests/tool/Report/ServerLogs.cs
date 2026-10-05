using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ID_Indetails.LoadTests.Tool.Report;

/// <summary>One kind of logged problem: what it is, how often, and when (per load step).</summary>
public sealed class LogGroup(string kind, string what)
{
    public string Kind { get; } = kind;

    public string What { get; } = what;

    public long Count { get; set; }

    public Dictionary<string, long> PerStep { get; } = [];
}

/// <summary>
/// The API's log (JSON console lines, as the load-test environment writes them) and nginx's access log, read for what
/// went wrong. Only message templates, levels, categories, exception types and error codes are kept: never a
/// message's values, so nothing a request carried ends up in a report.
/// </summary>
public static partial class ServerLogs
{
    public static List<LogGroup> ReadApi(string path, K6Summary run)
    {
        var groups = new Dictionary<(string, string), LogGroup>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            JsonElement entry;
            try
            {
                entry = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            var level = Text(entry, "LogLevel");
            var category = Text(entry, "Category");
            var template = entry.TryGetProperty("State", out var state) ? Text(state, "{OriginalFormat}") : string.Empty;
            var (kind, what) = Classify(level, category, template, entry, state);
            if (kind is null)
            {
                continue;
            }

            var group = groups.TryGetValue((kind, what), out var found) ? found : groups[(kind, what)] = new LogGroup(kind, what);
            group.Count++;
            if (DateTime.TryParse(Text(entry, "Timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var at)
                && run.StepAt(at) is { } step)
            {
                group.PerStep[step] = group.PerStep.GetValueOrDefault(step) + 1;
            }
        }

        return groups.Values.OrderByDescending(g => g.Count).ToList();
    }

    /// <summary>What a log line counts as, or nothing for ordinary information.</summary>
    private static (string? Kind, string What) Classify(string level, string category, string template, JsonElement entry, JsonElement state)
    {
        var exception = Text(entry, "Exception");
        if (exception.Length > 0)
        {
            return ("exception", $"{category}: {ExceptionKind(exception)}");
        }

        // Business and validation refusals the API logs at Information ("Request failed with {ErrorType}/{ErrorCode}").
        if (template.StartsWith("Request failed with", StringComparison.Ordinal))
        {
            return ("refusal", $"{Text(state, "ErrorType")}/{Text(state, "ErrorCode")}");
        }

        return level is "Warning" or "Error" or "Critical"
            ? (level.ToLowerInvariant(), $"{category}: {template}")
            : (null, string.Empty);
    }

    /// <summary>The exception's type, and for database errors what kind they are; never its message.</summary>
    public static string ExceptionKind(string exception)
    {
        var type = exception.Split([' ', ':', '\n', '\r'], 2)[0];
        var kind = exception switch
        {
            _ when exception.Contains("Deadlock found", StringComparison.Ordinal) => "deadlock",
            _ when exception.Contains("Lock wait timeout", StringComparison.Ordinal) => "lock wait timeout",
            _ when exception.Contains("Too many connections", StringComparison.Ordinal) => "too many database connections",
            _ when exception.Contains("All pooled connections are in use", StringComparison.Ordinal) => "connection pool exhausted",
            _ when exception.Contains("Duplicate entry", StringComparison.Ordinal) => "duplicate key",
            _ when exception.Contains("Timeout", StringComparison.OrdinalIgnoreCase) => "timeout",
            _ => null,
        };
        return kind is null ? type : $"{type} ({kind})";
    }

    public sealed record NginxEndpoint(string Name, long Count, long ServerErrors, double P50Ms, double P95Ms);

    public sealed record NginxResult(IReadOnlyList<NginxEndpoint> Endpoints, IReadOnlyDictionary<int, long> Statuses, IReadOnlyDictionary<string, long> ServerErrorsPerStep);

    /// <summary>nginx's access log: what the API took to answer, as seen right in front of it, and its 5xx answers.</summary>
    public static NginxResult ReadNginx(string path, K6Summary run)
    {
        var times = new Dictionary<string, List<double>>();
        var errors = new Dictionary<string, long>();
        var statuses = new SortedDictionary<int, long>();
        var errorsPerStep = new Dictionary<string, long>();
        foreach (var line in File.ReadLines(path))
        {
            var match = AccessLine().Match(line);
            if (!match.Success || !match.Groups["path"].Value.StartsWith("/api/", StringComparison.Ordinal))
            {
                continue;
            }

            var name = $"{match.Groups["method"].Value} {Route(match.Groups["path"].Value)}";
            var status = int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture);
            statuses[status] = statuses.GetValueOrDefault(status) + 1;
            (times.TryGetValue(name, out var list) ? list : times[name] = []).Add(double.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture) * 1000);
            if (status >= 500)
            {
                errors[name] = errors.GetValueOrDefault(name) + 1;
                // nginx writes "+0000"; .NET reads "+00:00".
                var stamp = match.Groups["at"].Value;
                stamp = stamp.Length > 5 ? $"{stamp[..^2]}:{stamp[^2..]}" : stamp;
                if (DateTime.TryParseExact(stamp, "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var at) && run.StepAt(at) is { } step)
                {
                    errorsPerStep[step] = errorsPerStep.GetValueOrDefault(step) + 1;
                }
            }
        }

        var endpoints = times.Select(t =>
        {
            var sorted = t.Value.Order().ToList();
            return new NginxEndpoint(t.Key, sorted.Count, errors.GetValueOrDefault(t.Key), Percentile(sorted, 0.5), Percentile(sorted, 0.95));
        }).OrderByDescending(e => e.P95Ms).ToList();
        return new NginxResult(endpoints, statuses, errorsPerStep);
    }

    /// <summary>The path as the k6 scripts name it: ids and dates taken out, no query.</summary>
    public static string Route(string path)
        => string.Join('/', path.Split('?')[0].Split('/').Select(part => IdPart().IsMatch(part) ? ":id" : DatePart().IsMatch(part) ? ":date" : part));

    private static double Percentile(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(p * sorted.Count) - 1)];

    private static string Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString()
            : string.Empty;

    [GeneratedRegex("""^\S+ \[(?<at>[^\]]+)\] "(?<method>[A-Z]+) (?<path>\S+) [^"]*" (?<status>\d{3}) \d+ (?<time>[\d.]+)""")]
    private static partial Regex AccessLine();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex IdPart();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePart();
}
