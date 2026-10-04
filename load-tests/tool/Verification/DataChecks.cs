using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool.Verification;

public sealed record CheckResult(string Name, long Violations);

/// <summary>
/// Rules the data must keep that the database itself doesn't enforce: what the API maintains across tables, and
/// what would mean records crossed between domains or went missing. Each query counts the rows breaking its rule.
/// They hold after seeding, and they must still hold after a load test.
/// </summary>
public static class DataChecks
{
    private static readonly (string Name, string Sql)[] Rules =
    [
        // Super admins may have no pay: the setup endpoint creates the first one without.
        ("Active people have a password, employment from the day they joined, and pay", """
            SELECT COUNT(*) FROM users u WHERE u.status = 'active' AND (
              u.password_hash IS NULL OR u.joined_on IS NULL
              OR NOT EXISTS (SELECT 1 FROM employment_periods e WHERE e.user_id = u.id AND e.started_on = u.joined_on)
              OR (u.role_code <> 'super_admin' AND NOT EXISTS (SELECT 1 FROM user_pay_history h WHERE h.user_id = u.id)))
            """),
        ("People who left have no open employment", """
            SELECT COUNT(*) FROM users u WHERE u.status = 'deactivated'
              AND EXISTS (SELECT 1 FROM employment_periods e WHERE e.user_id = u.id AND e.ended_on IS NULL)
            """),
        ("A project's progress matches its requirements", """
            SELECT COUNT(*) FROM projects p
            LEFT JOIN (SELECT project_id, COUNT(*) AS total, SUM(is_done) AS done
                       FROM project_requirements WHERE deleted_at IS NULL GROUP BY project_id) r ON r.project_id = p.id
            WHERE p.progress_pct <> CASE WHEN COALESCE(r.total, 0) = 0
                THEN IF(p.status IN ('completed', 'archived'), 100, 0)
                ELSE ROUND(r.done * 100 / r.total) END
            """),
        ("A completed project has its completion time", """
            SELECT COUNT(*) FROM projects WHERE status = 'completed' AND completed_at IS NULL
            """),
        ("Nobody's live work entries overlap", """
            SELECT COUNT(*) FROM work_entries a JOIN work_entries b
              ON b.user_id = a.user_id AND b.id > a.id AND b.started_at < a.ended_at AND a.started_at < b.ended_at
            WHERE a.deleted_at IS NULL AND b.deleted_at IS NULL
            """),
        ("Work is logged by Design people on Design projects", """
            SELECT COUNT(*) FROM work_entries w JOIN users u ON u.id = w.user_id JOIN projects p ON p.id = w.project_id
            WHERE p.domain_code <> 'design' OR COALESCE(u.domain_code, 'design') <> 'design'
            """),
        ("Requirement assignees work in the project's domain", """
            SELECT COUNT(*) FROM requirement_assignees a JOIN users u ON u.id = a.user_id
              JOIN project_requirements r ON r.id = a.requirement_id JOIN projects p ON p.id = r.project_id
            WHERE u.domain_code IS NOT NULL AND u.domain_code <> p.domain_code
            """),
        ("Materials, labour and statements belong to Execution projects", """
            SELECT (SELECT COUNT(*) FROM project_materials m JOIN projects p ON p.id = m.project_id WHERE p.domain_code <> 'execution')
                 + (SELECT COUNT(*) FROM labour_weeks l JOIN projects p ON p.id = l.project_id WHERE p.domain_code <> 'execution')
                 + (SELECT COUNT(*) FROM client_statements s JOIN projects p ON p.id = s.project_id WHERE p.domain_code <> 'execution')
            """),
        ("A statement lists only its own project's materials", """
            SELECT COUNT(*) FROM client_statement_lines l JOIN client_statements s ON s.id = l.statement_id
              JOIN project_materials m ON m.id = l.material_id WHERE m.project_id <> s.project_id
            """),
        ("Invoices and payments are for their project's client", """
            SELECT (SELECT COUNT(*) FROM invoices i JOIN projects p ON p.id = i.project_id WHERE NOT (i.client_id <=> p.client_id))
                 + (SELECT COUNT(*) FROM payments y JOIN projects p ON p.id = y.project_id WHERE NOT (y.client_id <=> p.client_id))
            """),
        ("An invoice's total is the sum of its lines", """
            SELECT COUNT(*) FROM invoices i
            WHERE i.total <> (SELECT COALESCE(SUM(l.amount), 0) FROM invoice_lines l WHERE l.invoice_id = i.id)
            """),
        ("Invoice numbers run without gaps per domain and year, and the sequence holds the last", """
            SELECT COUNT(*) FROM (
              SELECT p.domain_code AS domain, CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(i.number, '-', -2), '-', 1) AS UNSIGNED) AS yr,
                     COUNT(*) AS issued, MAX(CAST(SUBSTRING_INDEX(i.number, '-', -1) AS UNSIGNED)) AS last
              FROM invoices i JOIN projects p ON p.id = i.project_id WHERE i.number IS NOT NULL GROUP BY domain, yr) n
            LEFT JOIN invoice_sequences s ON s.domain_code = n.domain AND s.year = n.yr
            WHERE n.issued <> n.last OR NOT (s.last_number <=> n.last)
            """),
        ("Statement numbers run without gaps per year, and the sequence holds the last", """
            SELECT COUNT(*) FROM (
              SELECT CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(number, '-', -2), '-', 1) AS UNSIGNED) AS yr,
                     COUNT(*) AS made, MAX(CAST(SUBSTRING_INDEX(number, '-', -1) AS UNSIGNED)) AS last
              FROM client_statements GROUP BY yr) n
            LEFT JOIN statement_sequences s ON s.year = n.yr
            WHERE n.made <> n.last OR NOT (s.last_number <=> n.last)
            """),
        ("Live payments never come to more than their invoice", """
            SELECT COUNT(*) FROM (
              SELECT i.id FROM invoices i JOIN payments y ON y.invoice_id = i.id AND y.voided_at IS NULL
              GROUP BY i.id, i.total HAVING SUM(y.amount_usd) > i.total + 0.01) over_paid
            """),
        ("A payment's USD amount is its amount at its rate", """
            SELECT COUNT(*) FROM payments WHERE ABS(amount / fx_rate - amount_usd) > 0.01
            """),
    ];

    public static async Task<IReadOnlyList<CheckResult>> RunAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var results = new List<CheckResult>();
        foreach (var (name, sql) in Rules)
        {
            await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 600 };
            results.Add(new CheckResult(name, Convert.ToInt64(await command.ExecuteScalarAsync())));
        }

        return results;
    }
}
