using System.Globalization;

namespace ID_Indetails.LoadTests.Tool.Report;

/// <summary>A measure's peak and average, overall and in each load step.</summary>
public sealed record Measure(string What, string Unit, double Peak, double Average, IReadOnlyDictionary<string, double> PeakPerStep);

/// <summary>The server samples monitor.sh took during the run (monitor/*.csv).</summary>
public static class Samples
{
    public static List<Measure> Read(string folder, K6Summary run)
    {
        var measures = new List<Measure>();
        var host = Rows(Path.Combine(folder, "host.csv"));
        measures.Add(Of(host, run, "cpu_pct", "Server CPU", "%"));
        measures.Add(Of(host, run, "iowait_pct", "CPU waiting on disk", "%"));
        measures.Add(Of(host, run, "load1", "Load average (1 min)", ""));
        measures.Add(Of(host, run, "mem_used_mb", "Memory used", "MB"));
        measures.Add(Of(host, run, "swap_used_mb", "Swap used", "MB"));
        measures.Add(Of(host, run, "disk_read_kbs", "Disk reads", "KB/s"));
        measures.Add(Of(host, run, "disk_write_kbs", "Disk writes", "KB/s"));
        measures.Add(Of(host, run, "disk_busy_pct", "Disk busy", "%"));
        measures.Add(Of(host, run, "net_rx_kbs", "Network in", "KB/s"));
        measures.Add(Of(host, run, "net_tx_kbs", "Network out", "KB/s"));
        measures.Add(Of(host, run, "tcp_established", "Open TCP connections", ""));

        var mysql = Rows(Path.Combine(folder, "mysql.csv"));
        measures.Add(Of(mysql, run, "threads_connected", "Database connections", ""));
        measures.Add(Of(mysql, run, "threads_running", "Database threads running", ""));
        measures.Add(Growth(mysql, run, "questions", "Database queries", "/s", perSecond: true));
        measures.Add(Growth(mysql, run, "slow_queries", "Slow queries (> 0.5 s)", ""));
        measures.Add(Growth(mysql, run, "row_lock_waits", "Row lock waits", ""));
        measures.Add(Growth(mysql, run, "deadlocks", "Deadlocks", ""));
        // Pages the buffer pool didn't hold: many of these mean the data no longer fits in the database's memory.
        measures.Add(Growth(mysql, run, "pool_disk_reads", "Database pages read from disk", "/s", perSecond: true));
        measures.Add(Growth(mysql, run, "pool_read_requests", "Database pages read", "/s", perSecond: true));

        foreach (var container in Rows(Path.Combine(folder, "containers.csv")).GroupBy(r => r.GetValueOrDefault("container", "")).OrderBy(g => g.Key))
        {
            measures.Add(Of(container.ToList(), run, "cpu_pct", $"{container.Key} CPU", "%"));
            measures.Add(Of(container.ToList(), run, "mem_mb", $"{container.Key} memory", "MB"));
        }

        return measures.Where(m => m.Peak > 0 || m.Average > 0).ToList();
    }

    private static Measure Of(List<Dictionary<string, string>> rows, K6Summary run, string column, string what, string unit)
    {
        var values = rows.Select(r => (At: Time(r), Value: Number(r, column))).Where(v => v.Value is not null).Select(v => (v.At, Value: v.Value!.Value)).ToList();
        return new Measure(what, unit, values.Count == 0 ? 0 : values.Max(v => v.Value), values.Count == 0 ? 0 : values.Average(v => v.Value),
            values.GroupBy(v => run.StepAt(v.At)).Where(g => g.Key is not null).ToDictionary(g => g.Key!, g => g.Max(v => v.Value)));
    }

    /// <summary>For counters that only grow: how much they grew, overall and per step (or the peak rate, per second).</summary>
    private static Measure Growth(List<Dictionary<string, string>> rows, K6Summary run, string column, string what, string unit, bool perSecond = false)
    {
        var values = rows.Select(r => (At: Time(r), Value: Number(r, column))).Where(v => v.Value is not null).Select(v => (v.At, Value: v.Value!.Value)).ToList();
        var deltas = values.Zip(values.Skip(1), (a, b) => (b.At, Value: Math.Max(0, b.Value - a.Value) / (perSecond ? Math.Max(1, (b.At - a.At).TotalSeconds) : 1))).ToList();
        var perStep = deltas.GroupBy(d => run.StepAt(d.At)).Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!, g => perSecond ? g.Max(d => d.Value) : g.Sum(d => d.Value));
        return new Measure(what, unit, perSecond ? (deltas.Count == 0 ? 0 : deltas.Max(d => d.Value)) : deltas.Sum(d => d.Value),
            deltas.Count == 0 ? 0 : deltas.Average(d => d.Value), perStep);
    }

    private static List<Dictionary<string, string>> Rows(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = File.ReadAllLines(path);
        var header = lines.FirstOrDefault()?.Split(',') ?? [];
        return lines.Skip(1).Select(l => l.Split(',')).Where(c => c.Length == header.Length)
            .Select(c => header.Zip(c).ToDictionary(p => p.First, p => p.Second)).ToList();
    }

    private static DateTime Time(Dictionary<string, string> row)
        => DateTimeOffset.FromUnixTimeSeconds(long.Parse(row["ts"], CultureInfo.InvariantCulture)).UtcDateTime;

    private static double? Number(Dictionary<string, string> row, string column)
        => row.TryGetValue(column, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
