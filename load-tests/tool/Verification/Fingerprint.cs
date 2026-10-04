using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool.Verification;

/// <summary>
/// A digest of every table's rows, to show that seeding again with the same settings gives the same data. Password
/// and token hashes are left out: they are salted or random by design, so they differ on every run.
/// </summary>
public static class Fingerprint
{
    private static readonly HashSet<(string Table, string Column)> Random =
    [
        ("users", "password_hash"),
        ("refresh_tokens", "token_hash"),
        ("user_invitations", "token_hash"),
        ("password_reset_tokens", "token_hash"),
    ];

    /// <summary>One line per table, "table rows digest", then the digest of all of them.</summary>
    public static async Task<IReadOnlyList<string>> TakeAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var database = connection.Database;

        var columns = new List<(string Table, string Column)>();
        await using (var command = new MySqlCommand(
            "SELECT c.table_name, c.column_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema = @db ORDER BY c.table_name, c.ordinal_position", connection))
        {
            command.Parameters.AddWithValue("@db", database);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var lines = new List<string>();
        foreach (var table in columns.GroupBy(c => c.Table))
        {
            var values = table.Where(c => !Random.Contains(c)).Select(c => $"COALESCE(CAST(`{c.Column}` AS CHAR), '~')");
            await using var command = new MySqlCommand(
                $"SELECT COUNT(*), COALESCE(BIT_XOR(CRC32(CONCAT_WS('|', {string.Join(", ", values)}))), 0) FROM `{table.Key}`",
                connection) { CommandTimeout = 600 };
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            lines.Add($"{table.Key} {reader.GetInt64(0)} {Convert.ToUInt64(reader.GetValue(1)):x8}");
        }

        var all = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))[..16].ToLowerInvariant();
        lines.Add($"all {all}");
        return lines;
    }
}
