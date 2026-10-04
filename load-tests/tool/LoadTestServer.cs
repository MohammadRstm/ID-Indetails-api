using ID_Indetails.api.Data;
using ID_Indetails.api.Extensions;
using ID_Indetails.LoadTests.Tool.Safety;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool;

public sealed record MigrationStatus(int Applied, int Known, IReadOnlyList<string> Unknown)
{
    public bool UpToDate => Applied == Known && Unknown.Count == 0;
}

/// <summary>
/// The statements the tool runs on the load-test server. Database names in the SQL are the constants in
/// <see cref="LoadTestNames"/>, never input, so nothing here can name another database.
/// </summary>
public sealed class LoadTestServer(LoadTestSettings settings)
{
    private const string Database = LoadTestNames.Database;
    private const string IdentityDatabase = LoadTestNames.IdentityDatabase;

    public async Task<ServerIdentity> ReadIdentityAsync()
    {
        await using var connection = await OpenAsync(settings.ServerConnectionString);
        var hostname = await ScalarAsync<string>(connection, "SELECT @@hostname");
        var version = await ScalarAsync<string>(connection, "SELECT VERSION()");
        var databaseExists = await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = '{Database}'") > 0;
        var tables = await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{Database}'");

        return new ServerIdentity(hostname, version, await ReadMarkerAsync(connection), databaseExists, (int)tables);
    }

    public async Task CreateIdentityAsync(Guid instanceId)
    {
        await using var connection = await OpenAsync(settings.ServerConnectionString);
        await ExecuteAsync(connection,
            $"CREATE DATABASE IF NOT EXISTS `{IdentityDatabase}`;" +
            $"CREATE TABLE IF NOT EXISTS `{IdentityDatabase}`.`identity` (" +
            "  `id` TINYINT NOT NULL PRIMARY KEY CHECK (`id` = 1)," +
            "  `environment` VARCHAR(32) NOT NULL," +
            "  `instance_id` CHAR(36) NOT NULL," +
            "  `created_at` DATETIME(3) NOT NULL);");

        await using var insert = new MySqlCommand(
            $"INSERT INTO `{IdentityDatabase}`.`identity` (`id`, `environment`, `instance_id`, `created_at`) " +
            "VALUES (1, @environment, @instanceId, UTC_TIMESTAMP(3))", connection);
        insert.Parameters.AddWithValue("@environment", LoadTestNames.Environment);
        insert.Parameters.AddWithValue("@instanceId", instanceId.ToString());
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>With the server's settings (utf8mb4, as docker-compose.yml and production configure them).</summary>
    public async Task CreateDatabaseAsync()
    {
        await using var connection = await OpenAsync(settings.ServerConnectionString);
        await ExecuteAsync(connection, $"CREATE DATABASE IF NOT EXISTS `{Database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci");
    }

    public async Task DropDatabaseAsync()
    {
        await using var connection = await OpenAsync(settings.ServerConnectionString);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS `{Database}`");
    }

    public async Task DropIdentityAsync()
    {
        await using var connection = await OpenAsync(settings.ServerConnectionString);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS `{IdentityDatabase}`");
    }

    /// <summary>Applies the API's migrations, exactly as the production deployment does.</summary>
    public async Task MigrateAsync()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public async Task<MigrationStatus> ReadMigrationStatusAsync()
    {
        await using var context = NewContext();
        var known = context.Database.GetMigrations().ToHashSet();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        return new MigrationStatus(applied.Count(known.Contains), known.Count, applied.Where(m => !known.Contains(m)).ToList());
    }

    /// <summary>Whether the database holds no people yet: freshly reset, ready to seed.</summary>
    public async Task<bool> IsEmptyAsync()
    {
        await using var connection = await OpenAsync(settings.ConnectionString);
        return await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM `users`") == 0;
    }

    /// <summary>Rows per address column that hold an address outside <see cref="EmailSafety.Domain"/>.</summary>
    public async Task<IReadOnlyList<(string Column, long Rows)>> FindDeliverableAddressesAsync()
    {
        await using var connection = await OpenAsync(settings.ConnectionString);
        var findings = new List<(string, long)>();
        foreach (var (table, column) in EmailSafety.AddressColumns)
        {
            await using var command = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{table}` WHERE `{column}` REGEXP @pattern", connection);
            command.Parameters.AddWithValue("@pattern", EmailSafety.DeliverablePattern);
            var rows = Convert.ToInt64(await command.ExecuteScalarAsync());
            if (rows > 0)
            {
                findings.Add(($"{table}.{column}", rows));
            }
        }

        return findings;
    }

    private IdIndetailsDbContext NewContext() => new(
        new DbContextOptionsBuilder<IdIndetailsDbContext>()
            .UseMySql(settings.ConnectionString, DatabaseServiceCollectionExtensions.MySqlServerVersion)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static async Task<IdentityMarker?> ReadMarkerAsync(MySqlConnection connection)
    {
        var hasTable = await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '{IdentityDatabase}' AND table_name = 'identity'") > 0;
        if (!hasTable)
        {
            return null;
        }

        await using var command = new MySqlCommand($"SELECT `environment`, `instance_id` FROM `{IdentityDatabase}`.`identity`", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        // MySqlConnector reads CHAR(36) as a Guid by default, but a connection string can change that.
        var instance = Convert.ToString(reader.GetValue(1));
        return new IdentityMarker(reader.GetString(0), Guid.TryParse(instance, out var id) ? id : Guid.Empty);
    }

    private static async Task<MySqlConnection> OpenAsync(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
