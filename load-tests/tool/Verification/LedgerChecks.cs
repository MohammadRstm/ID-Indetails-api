using System.Text.Json;
using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool.Verification;

public sealed record Expectation(string What, long Expected, long Actual)
{
    public bool Holds => Expected == Actual;
}

/// <summary>
/// What a load test changed, from its ledger, against what the database holds afterwards. Every record the run made
/// carries its mark ("[LT run id]"), so the database can be asked how many there are: more than the ledger says means
/// duplicates (or a request that timed out but still wrote), fewer means lost writes. Then the final state of each:
/// deleted or not, done or not, voided or not, and whose it is.
/// </summary>
public static class LedgerChecks
{
    /// <summary>The ledger kind that creates a record, its table, and the column that carries the run's mark.</summary>
    private static readonly (string Kind, string Table, string MarkColumn)[] Created =
    [
        ("work_entry.created", "work_entries", "description"),
        ("requirement.created", "project_requirements", "text"),
        ("meeting.created", "project_meetings", "topic"),
        ("material.created", "project_materials", "note"),
        ("client.created", "clients", "note"),
        ("payment.recorded", "payments", "note"),
        ("expense.created", "project_expenses", "label"),
        ("hour_excuse.created", "hour_excuses", "note"),
        ("comment.created", "work_entry_comments", "body"),
        ("message.sent", "messages", "body"),
    ];

    /// <summary>Kinds whose records are soft-deleted, with the ledger kinds that delete and restore them.</summary>
    private static readonly (string Table, string Created, string Deleted, string? Restored)[] SoftDeleted =
    [
        ("work_entries", "work_entry.created", "work_entry.deleted", "work_entry.restored"),
        ("project_requirements", "requirement.created", "requirement.deleted", null),
        ("project_meetings", "meeting.created", "meeting.deleted", null),
        ("project_materials", "material.created", "material.deleted", null),
        ("project_expenses", "expense.created", "expense.deleted", null),
        ("hour_excuses", "hour_excuse.created", "hour_excuse.deleted", null),
    ];

    /// <summary>Whose a record is: the ledger's "user" must be this column.</summary>
    private static readonly (string Kind, string Table, string OwnerColumn)[] Owned =
    [
        ("work_entry.created", "work_entries", "user_id"),
        ("material.created", "project_materials", "added_by_id"),
        ("client.created", "clients", "created_by_id"),
        ("message.sent", "messages", "sender_id"),
        ("payment.recorded", "payments", "recorded_by_id"),
        ("comment.created", "work_entry_comments", "author_id"),
    ];

    public static async Task<IReadOnlyList<Expectation>> CompareAsync(string connectionString, string runId, IReadOnlyList<JsonElement> ledger)
    {
        var mark = $"%[LT run {runId}]%";
        var mine = ledger.Where(e => Text(e, "run") == runId).OrderBy(e => e.GetProperty("t").GetInt64()).ToList();
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var results = new List<Expectation>();

        foreach (var (kind, table, column) in Created)
        {
            var made = Ids(mine, kind).Count;
            results.Add(new($"{table}: records this run made", made,
                await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}` WHERE `{column}` LIKE @mark", mark)));
        }

        var drafted = Ids(mine, "invoice.drafted");
        var discarded = Ids(mine, "invoice.discarded");
        results.Add(new("invoices: drafts this run kept (discarded ones are removed)", drafted.Except(discarded).Count(),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM `invoices` WHERE `notes` LIKE @mark", mark)));
        results.Add(new("invoices: drafts this run issued", Ids(mine, "invoice.issued").Count,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM `invoices` WHERE `notes` LIKE @mark AND `number` IS NOT NULL", mark)));
        results.Add(new("payments: voided by this run", Ids(mine, "payment.voided").Count,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM `payments` WHERE `note` LIKE @mark AND `voided_at` IS NOT NULL", mark)));

        var weeks = mine.Where(e => Text(e, "kind") == "labour_week.saved")
            .Select(e => (e.GetProperty("project").GetInt64(), Text(e, "week"))).Distinct().Count();
        results.Add(new("labour_weeks: weeks this run reported", weeks,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM `labour_weeks` WHERE `note` LIKE @mark", mark)));

        foreach (var (table, created, deleted, restored) in SoftDeleted)
        {
            var gone = FinalDeleted(mine, created, deleted, restored);
            results.Add(new($"{table}: deleted at the end, as the run left them", gone.Count,
                await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}` WHERE `{MarkColumnOf(table)}` LIKE @mark AND `deleted_at` IS NOT NULL", mark)));
        }

        results.Add(await TickedAsync(connection, mine));
        foreach (var (kind, table, column) in Owned)
        {
            results.Add(await OwnedAsync(connection, mine, kind, table, column));
        }

        results.Add(await LastEditsAsync(connection, mine));
        return results;
    }

    private static string MarkColumnOf(string table) => Created.First(c => c.Table == table).MarkColumn;

    private static List<long> Ids(IEnumerable<JsonElement> entries, string kind)
        => entries.Where(e => Text(e, "kind") == kind && e.TryGetProperty("id", out _)).Select(e => e.GetProperty("id").GetInt64()).Distinct().ToList();

    /// <summary>Records the run created whose last delete or restore was a delete.</summary>
    private static List<long> FinalDeleted(List<JsonElement> entries, string created, string deleted, string? restored)
    {
        var made = Ids(entries, created).ToHashSet();
        var state = new Dictionary<long, bool>();
        foreach (var entry in entries.Where(e => e.TryGetProperty("id", out _)))
        {
            var kind = Text(entry, "kind");
            if (kind == deleted || kind == restored)
            {
                state[entry.GetProperty("id").GetInt64()] = kind == deleted;
            }
        }

        return state.Where(s => s.Value && made.Contains(s.Key)).Select(s => s.Key).ToList();
    }

    /// <summary>Requirements the run ticked or unticked are done exactly when its last action on each says so.</summary>
    private static async Task<Expectation> TickedAsync(MySqlConnection connection, List<JsonElement> entries)
    {
        var last = new Dictionary<long, bool>();
        foreach (var entry in entries.Where(e => Text(e, "kind") is "requirement.done" or "requirement.undone"))
        {
            last[entry.GetProperty("id").GetInt64()] = Text(entry, "kind") == "requirement.done";
        }

        if (last.Count == 0)
        {
            return new("project_requirements: done as the run's last tick left them", 0, 0);
        }

        var done = await ScalarAsync(connection,
            $"SELECT COUNT(*) FROM `project_requirements` WHERE `is_done` = 1 AND `id` IN ({string.Join(',', last.Keys)})", null);
        return new("project_requirements: done as the run's last tick left them", last.Count(l => l.Value), done);
    }

    /// <summary>Every record the run made belongs to the person who made it.</summary>
    private static async Task<Expectation> OwnedAsync(MySqlConnection connection, List<JsonElement> entries, string kind, string table, string column)
    {
        var expected = entries.Where(e => Text(e, "kind") == kind && e.TryGetProperty("id", out _) && e.TryGetProperty("user", out _))
            .GroupBy(e => e.GetProperty("id").GetInt64())
            .ToDictionary(g => g.Key, g => g.First().GetProperty("user").GetInt64());
        var matching = 0L;
        foreach (var chunk in expected.Keys.Chunk(1000))
        {
            await using var command = new MySqlCommand($"SELECT `id`, `{column}` FROM `{table}` WHERE `id` IN ({string.Join(',', chunk)})", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(1) && reader.GetInt64(1) == expected[reader.GetInt64(0)])
                {
                    matching++;
                }
            }
        }

        return new($"{table}: each record the run made belongs to its maker", expected.Count, matching);
    }

    /// <summary>
    /// A project several people edit holds the last edit. Requests on one project are applied one after another, so
    /// a mismatch means an edit was lost or applied out of order.
    /// </summary>
    private static async Task<Expectation> LastEditsAsync(MySqlConnection connection, List<JsonElement> entries)
    {
        var last = new Dictionary<long, string>();
        foreach (var entry in entries.Where(e => Text(e, "kind") == "project.updated"))
        {
            last[entry.GetProperty("id").GetInt64()] = Text(entry, "description") ?? string.Empty;
        }

        var matching = 0L;
        foreach (var chunk in last.Keys.Chunk(1000))
        {
            await using var command = new MySqlCommand($"SELECT `id`, `description` FROM `projects` WHERE `id` IN ({string.Join(',', chunk)})", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(1) && reader.GetString(1) == last[reader.GetInt64(0)])
                {
                    matching++;
                }
            }
        }

        return new("projects: each edited project shows its last edit", last.Count, matching);
    }

    private static string? Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql, string? mark)
    {
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 600 };
        if (mark is not null)
        {
            command.Parameters.AddWithValue("@mark", mark);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
