using ID_Indetails.api.Data;
using ID_Indetails.api.Extensions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ID_Indetails.IntegrationTests.Data;

/// <summary>
/// Creates a throwaway database, applies the real migrations to it, and drops it afterwards.
///
/// These run against real MySQL on purpose: CHECK constraints, composite foreign keys,
/// generated columns and delete rules are provider behaviour that an in-memory or SQLite
/// double would not reproduce.
/// </summary>
public sealed class MigratedDatabaseFixture : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;

    // Kept short: EF Core's migration lock name embeds it, and MySQL caps lock names at 64 characters.
    public string DatabaseName { get; } = $"idi_test_{Guid.NewGuid().ToString("N")[..12]}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _adminConnectionString = TestDatabaseSettings.AdminConnectionString();

        await using (var admin = new MySqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new MySqlCommand($"CREATE DATABASE `{DatabaseName}`", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new MySqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = DatabaseName,
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public IdIndetailsDbContext CreateContext() => new(
        new DbContextOptionsBuilder<IdIndetailsDbContext>()
            .UseMySql(ConnectionString, DatabaseServiceCollectionExtensions.MySqlServerVersion)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task<MySqlConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(_adminConnectionString))
        {
            return;
        }

        MySqlConnection.ClearAllPools();
        await using var admin = new MySqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{DatabaseName}`", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
