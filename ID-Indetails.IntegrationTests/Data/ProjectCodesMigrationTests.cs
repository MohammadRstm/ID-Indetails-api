using ID_Indetails.api.Business.Projects;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Data;

/// <summary>
/// The ProjectCodes migration on a database that already has projects, as production does: its own database, migrated
/// to the migration before, given projects, then migrated the rest of the way.
/// </summary>
public sealed class ProjectCodesMigrationTests
{
    private const string DatabaseName = "idi_test_codes";

    [Fact]
    public async Task Existing_projects_are_numbered_per_domain_and_quarter_of_their_start_and_take_their_names_initials()
    {
        await using var admin = new MySqlConnection(TestDatabaseSettings.AdminConnectionString());
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS `{DatabaseName}`; CREATE DATABASE `{DatabaseName}`;");
        try
        {
            var connectionString = MigratedDatabaseFixture.ConnectionTo(DatabaseName);
            await using var context = MigratedDatabaseFixture.NewContext(connectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261115120000_PayChangeHolds");

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            // owners and clients play no part in the codes
            await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS = 0;\n" + string.Concat(
                Project(1, "design", "2026-07-06", "2026-07-06", "Achrafieh duplex"),
                Project(2, "design", "2026-09-14", "2026-09-14", "Bayt Capital HQ structural assessment"),
                Project(3, "design", "2026-08-03", "2026-08-03", "Atelier Rouge showroom extension"),
                Project(4, "design", "2026-06-08", "2026-06-08", "Faqra mountain villa"),
                Project(5, "execution", "2026-07-06", "2026-07-06", "Rawda Residences, Block A"),
                Project(6, "execution", "2022-08-04", "2026-11-06", "Villa Kadmous Finishing"),
                Project(7, "execution", "2025-01-06", "2026-10-30", "Residence Mila 2"),
                Project(8, "execution", "2025-01-06", "2026-10-30", "Mila 2 Concrete"),
                Project(9, "execution", "2025-01-06", "2026-10-29", "Beit el Hana 3"),
                Project(10, "design", "2026-11-05", "2026-11-05", "مشروع"),
                Project(11, "design", "2026-12-31", "2026-12-31", "Harbour Offices, floor 3, new tower, block A, east wing, level 12")));

            await migrator.MigrateAsync();

            var codes = await CodesAsync(connection);
            codes.ShouldBe(new Dictionary<long, string>
            {
                [1] = "ID26Q3001-AD",
                [3] = "ID26Q3002-ARSE",
                [2] = "ID26Q3003-BCHSA",
                [4] = "ID26Q2001-FMV",
                // each domain numbers its own projects
                [5] = "ID26Q3001-RRBA",
                [6] = "ID22Q3001-VKF",
                // the same start day: the one added first comes first
                [9] = "ID25Q1001-BH3",
                [7] = "ID25Q1002-RM2",
                [8] = "ID25Q1003-M2C",
                [10] = "ID26Q4001-X",
                [11] = "ID26Q4002-HOF3NTBAEW",
            }, ignoreOrder: true);

            // the building codes are what the app suggests for those names
            foreach (var (id, title) in new Dictionary<long, string>
            {
                [2] = "Bayt Capital HQ structural assessment", [5] = "Rawda Residences, Block A", [9] = "Beit el Hana 3",
                [10] = "مشروع", [11] = "Harbour Offices, floor 3, new tower, block A, east wing, level 12",
            })
            {
                codes[id].Split('-')[1].ShouldBe(ProjectCodeRules.BuildingCodeOf(title));
            }
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS `{DatabaseName}`;");
        }
    }

    private static string Project(long id, string domain, string start, string created, string title)
    {
        var site = domain == "execution";
        return "INSERT INTO `projects` (`id`, `domain_code`, `title`, `status`, `billing_type`, `owner_id`, `start_date`, " +
            "`revenue`, `revenue_currency`, `progress_pct`, `created_at`, `project_fee`, `permits_price`) VALUES " +
            $"({id}, '{domain}', '{title}', 'active', '{(site ? "client" : "internal")}', 1, '{start}', 0, 'USD', 0, " +
            $"'{created} 09:00:00', {(site ? "0" : "NULL")}, {(site ? "0" : "NULL")});\n";
    }

    private static async Task<Dictionary<long, string>> CodesAsync(MySqlConnection connection)
    {
        var codes = new Dictionary<long, string>();
        await using var command = new MySqlCommand("SELECT `id`, CONCAT(`project_code`, '-', `building_code`) FROM `projects`", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            codes[reader.GetInt64(0)] = reader.GetString(1);
        }

        return codes;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
