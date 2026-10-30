using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool.Verification;

public sealed record CountResult(string What, int Expected, long Actual)
{
    public bool Matches => Expected == Actual;
}

/// <summary>What the seeder counted it created, against what the database holds.</summary>
public static class SeedCounts
{
    private static readonly (string What, string Sql)[] Queries =
    [
        ("users.super_admin", "SELECT COUNT(*) FROM users WHERE role_code = 'super_admin'"),
        ("users.admin", "SELECT COUNT(*) FROM users WHERE role_code = 'admin'"),
        ("users.employee", "SELECT COUNT(*) FROM users WHERE role_code = 'employee'"),
        ("users.deactivated", "SELECT COUNT(*) FROM users WHERE status = 'deactivated'"),
        ("jokers", "SELECT COUNT(DISTINCT user_id) FROM user_permission_overrides"),
        ("pay_records", "SELECT COUNT(*) FROM user_pay_history"),
        ("schedules", "SELECT COUNT(*) FROM user_schedules"),
        ("hour_excuses", "SELECT COUNT(*) FROM hour_excuses"),
        ("teams", "SELECT COUNT(*) FROM teams"),
        ("overhead_items", "SELECT COUNT(*) FROM overhead_items"),
        ("clients", "SELECT COUNT(*) FROM clients"),
        ("projects.design", "SELECT COUNT(*) FROM projects WHERE domain_code = 'design'"),
        ("projects.execution", "SELECT COUNT(*) FROM projects WHERE domain_code = 'execution'"),
        ("requirements", "SELECT COUNT(*) FROM project_requirements"),
        ("meetings", "SELECT COUNT(*) FROM project_meetings"),
        ("expenses", "SELECT COUNT(*) FROM project_expenses"),
        ("work_entries", "SELECT COUNT(*) FROM work_entries WHERE deleted_at IS NULL"),
        ("work_entry_comments", "SELECT COUNT(*) FROM work_entry_comments"),
        ("site_expenses", "SELECT COUNT(*) FROM site_expenses WHERE deleted_at IS NULL"),
        ("labour_weeks", "SELECT COUNT(*) FROM labour_weeks"),
        ("boqs", "SELECT COUNT(*) FROM boqs WHERE deleted_at IS NULL"),
        ("invoices", "SELECT COUNT(*) FROM invoices"),
        ("invoices.voided", "SELECT COUNT(*) FROM invoices WHERE voided_at IS NOT NULL"),
        ("payments", "SELECT COUNT(*) FROM payments"),
        ("messages", "SELECT COUNT(*) FROM messages"),
    ];

    public static async Task<IReadOnlyList<CountResult>> CompareAsync(string connectionString, IReadOnlyDictionary<string, int> expected)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var results = new List<CountResult>();
        foreach (var (what, sql) in Queries)
        {
            await using var command = new MySqlCommand(sql, connection);
            results.Add(new CountResult(what, expected.GetValueOrDefault(what), Convert.ToInt64(await command.ExecuteScalarAsync())));
        }

        return results;
    }
}
