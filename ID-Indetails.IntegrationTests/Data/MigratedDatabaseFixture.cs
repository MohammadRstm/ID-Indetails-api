using System.Text;
using ID_Indetails.api.Data;
using ID_Indetails.api.Extensions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

// Every test class uses the same database (see MigratedDatabaseFixture), so the classes run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ID_Indetails.IntegrationTests.Data;

/// <summary>
/// Gives a test class the test database, empty apart from the rows the migrations seed (roles, permissions, domains).
///
/// The database is created and migrated once per test run, not once per class: building the schema costs far more
/// than the tests themselves. Before each class, every row is deleted and the seed rows are copied back from a
/// snapshot taken right after migrating. The tests of one class share their data; classes never see each other's.
///
/// These run against real MySQL on purpose: CHECK constraints, composite foreign keys, generated columns and delete
/// rules are provider behaviour that an in-memory or SQLite double would not reproduce.
/// </summary>
public sealed class MigratedDatabaseFixture : IAsyncLifetime
{
    // Kept short: EF Core's migration lock name embeds it, and MySQL caps lock names at 64 characters.
    public const string DatabaseName = "idi_test_run";
    private const string SeedDatabaseName = "idi_test_seed";

    /// <summary>Prepares the database once per run, and gives the statements that empty it again.</summary>
    private static readonly Lazy<Task<string>> ResetSql = new(PrepareAsync);

    public string ConnectionString { get; } = ConnectionTo(DatabaseName);

    public async Task InitializeAsync()
    {
        var resetSql = await ResetSql.Value;
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, resetSql);
    }

    // The database stays for the next class; the next run drops it.
    public Task DisposeAsync() => Task.CompletedTask;

    public IdIndetailsDbContext CreateContext() => NewContext(ConnectionString);

    public async Task<MySqlConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Drops the test databases earlier runs left behind, migrates a new one and snapshots its seed rows.
    /// </summary>
    private static async Task<string> PrepareAsync()
    {
        await using (var admin = new MySqlConnection(TestDatabaseSettings.AdminConnectionString()))
        {
            await admin.OpenAsync();
            var old = await ListAsync(admin, "SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 'idi\\_test\\_%'");
            foreach (var database in old)
            {
                await ExecuteAsync(admin, $"DROP DATABASE `{database}`");
            }

            await ExecuteAsync(admin, $"CREATE DATABASE `{DatabaseName}`");
            await ExecuteAsync(admin, $"CREATE DATABASE `{SeedDatabaseName}`");
        }

        await using (var context = NewContext(ConnectionTo(DatabaseName)))
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new MySqlConnection(ConnectionTo(DatabaseName));
        await connection.OpenAsync();
        var tables = await ListAsync(connection,
            "SELECT table_name FROM information_schema.tables " +
            $"WHERE table_schema = '{DatabaseName}' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory' " +
            "ORDER BY table_name");

        // One transaction, so emptying every table costs one commit rather than one per table.
        var reset = new StringBuilder("SET FOREIGN_KEY_CHECKS = 0; START TRANSACTION;\n");
        var restore = new StringBuilder();
        foreach (var table in tables)
        {
            reset.Append($"DELETE FROM `{table}`;\n");
            if (!await HasRowsAsync(connection, table))
            {
                continue;
            }

            await ExecuteAsync(connection, $"CREATE TABLE `{SeedDatabaseName}`.`{table}` AS SELECT * FROM `{table}`");

            // Generated columns can't be written; they fill themselves in again.
            var columns = string.Join(", ", (await ListAsync(connection,
                    "SELECT column_name FROM information_schema.columns " +
                    $"WHERE table_schema = '{DatabaseName}' AND table_name = '{table}' AND COALESCE(generation_expression, '') = '' " +
                    "ORDER BY ordinal_position"))
                .Select(column => $"`{column}`"));
            restore.Append($"INSERT INTO `{table}` ({columns}) SELECT {columns} FROM `{SeedDatabaseName}`.`{table}`;\n");
        }

        return reset.Append(restore).Append("COMMIT; SET FOREIGN_KEY_CHECKS = 1;").ToString();
    }

    private static IdIndetailsDbContext NewContext(string connectionString) => new(
        new DbContextOptionsBuilder<IdIndetailsDbContext>()
            .UseMySql(connectionString, DatabaseServiceCollectionExtensions.MySqlServerVersion)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static string ConnectionTo(string database)
        => new MySqlConnectionStringBuilder(TestDatabaseSettings.AdminConnectionString()) { Database = database }.ConnectionString;

    private static async Task<bool> HasRowsAsync(MySqlConnection connection, string table)
    {
        await using var command = new MySqlCommand($"SELECT EXISTS (SELECT 1 FROM `{table}`)", connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<List<string>> ListAsync(MySqlConnection connection, string sql)
    {
        var values = new List<string>();
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
