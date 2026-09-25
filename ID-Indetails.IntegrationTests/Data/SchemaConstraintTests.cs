using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Data;

/// <summary>
/// Applies the InitialSchema migration to a real MySQL database and checks that the rules
/// the schema is meant to enforce are actually enforced by MySQL.
/// </summary>
public class SchemaConstraintTests(MigratedDatabaseFixture db) : IClassFixture<MigratedDatabaseFixture>
{
    private const string Now = "UTC_TIMESTAMP(3)";

    // ---------- seed data ----------

    [Fact]
    public async Task Migration_seeds_the_reference_data()
    {
        await using var context = db.CreateContext();

        (await context.Domains.CountAsync()).ShouldBe(DomainCodes.All.Count);
        (await context.Roles.CountAsync()).ShouldBe(RoleCodes.All.Count);
        (await context.Permissions.CountAsync()).ShouldBe(PermissionCatalog.Definitions.Count);
        (await context.RolePermissions.CountAsync()).ShouldBe(PermissionCatalog.Grants.Sum(g => g.Value.Count));
    }

    // ---------- users ----------

    [Fact]
    public async Task Super_admin_cannot_belong_to_a_domain()
    {
        await ShouldViolateAsync(
            UserSql(RoleCodes.SuperAdmin, $"'{DomainCodes.Design}'"),
            "ck_users_super_admin_has_no_domain");
    }

    [Fact]
    public async Task Admins_and_employees_must_belong_to_a_domain()
    {
        await ShouldViolateAsync(UserSql(RoleCodes.Employee, "NULL"), "ck_users_super_admin_has_no_domain");
        await ShouldViolateAsync(UserSql(RoleCodes.Admin, "NULL"), "ck_users_super_admin_has_no_domain");
    }

    [Fact]
    public async Task Super_admin_without_a_domain_is_accepted()
    {
        (await InsertAsync(UserSql(RoleCodes.SuperAdmin, "NULL"))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Invited_user_may_have_no_password_or_name_but_an_active_user_may_not()
    {
        (await InsertAsync(
            $"INSERT INTO users (email, role_code, domain_code, status, must_change_password, created_at) " +
            $"VALUES ('{Email()}', 'employee', 'design', 'invited', 0, {Now})")).ShouldBeGreaterThan(0);

        await ShouldViolateAsync(
            $"INSERT INTO users (email, role_code, domain_code, status, must_change_password, created_at) " +
            $"VALUES ('{Email()}', 'employee', 'design', 'active', 0, {Now})",
            "ck_users_active_has_credentials");
    }

    [Fact]
    public async Task Email_is_unique_across_the_whole_company()
    {
        var email = Email();
        await InsertAsync(UserSql(RoleCodes.Employee, "'design'", email));

        var ex = await Should.ThrowAsync<MySqlException>(() =>
            ExecuteAsync(UserSql(RoleCodes.Employee, "'execution'", email)));
        ex.Number.ShouldBe(1062); // duplicate entry
    }

    [Fact]
    public async Task Invalid_enum_value_is_rejected()
    {
        await ShouldViolateAsync(
            $"INSERT INTO users (email, role_code, domain_code, status, must_change_password, created_at) " +
            $"VALUES ('{Email()}', 'employee', 'design', 'suspended', 0, {Now})",
            "ck_users_status");
    }

    // ---------- projects and clients ----------

    [Fact]
    public async Task Internal_project_cannot_have_a_client_or_revenue()
    {
        var owner = await ActiveUserAsync("design");
        var client = await ClientAsync("design");

        await ShouldViolateAsync(
            ProjectSql("design", owner, clientId: client.ToString(), billing: "internal", revenue: 0),
            "ck_projects_internal_has_no_client_or_revenue");

        await ShouldViolateAsync(
            ProjectSql("design", owner, clientId: "NULL", billing: "internal", revenue: 5000),
            "ck_projects_internal_has_no_client_or_revenue");

        (await InsertAsync(ProjectSql("design", owner, clientId: "NULL", billing: "internal", revenue: 0)))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Project_cannot_use_a_client_from_the_other_domain()
    {
        var owner = await ActiveUserAsync("design");
        var executionClient = await ClientAsync("execution");

        await ShouldViolateAsync(
            ProjectSql("design", owner, clientId: executionClient.ToString(), billing: "client", revenue: 1000),
            "fk_projects_clients_client_id_domain_code");
    }

    [Fact]
    public async Task Project_can_use_a_client_from_its_own_domain()
    {
        var owner = await ActiveUserAsync("design");
        var client = await ClientAsync("design");

        (await InsertAsync(ProjectSql("design", owner, clientId: client.ToString(), billing: "client", revenue: 1000)))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Client_with_projects_cannot_be_deleted()
    {
        var owner = await ActiveUserAsync("design");
        var client = await ClientAsync("design");
        await InsertAsync(ProjectSql("design", owner, clientId: client.ToString(), billing: "client", revenue: 1000));

        var ex = await Should.ThrowAsync<MySqlException>(() =>
            ExecuteAsync($"DELETE FROM clients WHERE id = {client}"));
        ex.Number.ShouldBe(1451); // row is referenced by a foreign key
    }

    [Fact]
    public async Task Progress_cannot_exceed_100()
    {
        var owner = await ActiveUserAsync("design");
        var project = await InsertAsync(ProjectSql("design", owner, "NULL", "internal", 0));

        await ShouldViolateAsync(
            $"UPDATE projects SET progress_pct = 101 WHERE id = {project}",
            "ck_projects_progress_range");
    }

    // ---------- work entries and site costs ----------

    [Fact]
    public async Task Work_entry_must_end_after_it_starts_and_its_duration_is_generated()
    {
        var user = await ActiveUserAsync("design");
        var project = await InsertAsync(ProjectSql("design", user, "NULL", "internal", 0));

        await ShouldViolateAsync(
            WorkEntrySql(user, project, "2026-09-21 10:00:00", "2026-09-21 09:00:00"),
            "ck_work_entries_ends_after_start");

        var entry = await InsertAsync(WorkEntrySql(user, project, "2026-09-21 09:00:00", "2026-09-21 10:30:00"));
        (await ScalarAsync<int>($"SELECT duration_minutes FROM work_entries WHERE id = {entry}")).ShouldBe(90);
    }

    [Fact]
    public async Task Labour_week_must_start_on_a_monday_and_day_cost_is_generated()
    {
        var user = await ActiveUserAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, "NULL", "internal", 0));

        // 2026-09-22 is a Tuesday.
        await ShouldViolateAsync(LabourWeekSql(project, user, "2026-09-22"), "ck_labour_weeks_starts_on_monday");

        var week = await InsertAsync(LabourWeekSql(project, user, "2026-09-21"));
        var day = await InsertAsync(
            $"INSERT INTO labour_week_days (labour_week_id, work_date, workers, daily_pay) VALUES ({week}, '2026-09-21', 12, 45.50)");

        (await ScalarAsync<decimal>($"SELECT cost FROM labour_week_days WHERE id = {day}")).ShouldBe(546.00m);
    }

    [Fact]
    public async Task One_labour_report_per_project_per_week()
    {
        var user = await ActiveUserAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, "NULL", "internal", 0));
        await InsertAsync(LabourWeekSql(project, user, "2026-09-14"));

        var ex = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(LabourWeekSql(project, user, "2026-09-14")));
        ex.Number.ShouldBe(1062);
    }

    [Fact]
    public async Task Material_total_is_generated()
    {
        var user = await ActiveUserAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, "NULL", "internal", 0));

        var material = await InsertAsync(
            "INSERT INTO project_materials (project_id, name, quantity, unit, unit_cost, currency, category, added_at, added_by_id) " +
            $"VALUES ({project}, 'Rebar', 2.5, 'tonne', 10.10, 'USD', 'steel', {Now}, {user})");

        (await ScalarAsync<decimal>($"SELECT total FROM project_materials WHERE id = {material}")).ShouldBe(25.25m);
    }

    // ---------- billing ----------

    [Fact]
    public async Task Invoice_is_numbered_only_when_issued()
    {
        var owner = await ActiveUserAsync("design");
        var client = await ClientAsync("design");
        var project = await InsertAsync(ProjectSql("design", owner, client.ToString(), "client", 1000));

        await ShouldViolateAsync(
            "INSERT INTO invoices (project_id, client_id, number, currency, total, terms_days, created_at) " +
            $"VALUES ({project}, {client}, 'HL-2026-001', 'USD', 500, 30, {Now})",
            "ck_invoices_numbered_when_issued");

        (await InsertAsync(
            "INSERT INTO invoices (project_id, client_id, currency, total, terms_days, created_at) " +
            $"VALUES ({project}, {client}, 'USD', 500, 30, {Now})")).ShouldBeGreaterThan(0);
    }

    // ---------- company layer and messaging ----------

    [Fact]
    public async Task Company_settings_can_only_hold_one_row()
    {
        await ShouldViolateAsync(
            "INSERT INTO company_settings (id, name, time_zone, week_start, use_24_hour_time, default_currency, overtime_multiplier) " +
            "VALUES (2, 'Second company', 'Asia/Beirut', 1, 0, 'USD', 1)",
            "ck_company_settings_single_row");
    }

    [Fact]
    public async Task Direct_message_is_stored_once_per_pair_lowest_id_first()
    {
        var a = await ActiveUserAsync("design");
        var b = await ActiveUserAsync("design");
        var (low, high) = a < b ? (a, b) : (b, a);

        await ShouldViolateAsync(
            $"INSERT INTO conversations (type, dm_user_low_id, dm_user_high_id, created_at) VALUES ('dm', {high}, {low}, {Now})",
            "ck_conversations_shape");

        (await InsertAsync(
            $"INSERT INTO conversations (type, dm_user_low_id, dm_user_high_id, created_at) VALUES ('dm', {low}, {high}, {Now})"))
            .ShouldBeGreaterThan(0);
    }

    // ---------- EF Core mapping round trip ----------

    [Fact]
    public async Task Enums_are_stored_as_snake_case_and_datetimes_read_back_as_utc()
    {
        var at = new DateTime(2026, 9, 25, 8, 30, 0, DateTimeKind.Utc);

        await using (var context = db.CreateContext())
        {
            context.DomainSettings.Add(new DomainSettings
            {
                DomainCode = DomainCodes.Design,
                EditWindow = EditWindow.SevenDays,
                NotifyEntry = EntryNotificationMode.Digest,
                UpdatedAt = at,
            });
            await context.SaveChangesAsync();
        }

        (await ScalarAsync<string>("SELECT edit_window FROM domain_settings WHERE domain_code = 'design'"))
            .ShouldBe("seven_days");

        await using (var context = db.CreateContext())
        {
            var settings = await context.DomainSettings.SingleAsync(s => s.DomainCode == DomainCodes.Design);

            settings.EditWindow.ShouldBe(EditWindow.SevenDays);
            settings.NotifyEntry.ShouldBe(EntryNotificationMode.Digest);
            settings.UpdatedAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
            settings.UpdatedAt.ShouldBe(at);
        }
    }

    // ---------- helpers ----------

    private static string Email() => $"{Guid.NewGuid():N}@test.local";

    private static string UserSql(string role, string domainSql, string? email = null) =>
        "INSERT INTO users (email, password_hash, first_name, last_name, role_code, domain_code, status, must_change_password, created_at) " +
        $"VALUES ('{email ?? Email()}', 'hash', 'Test', 'User', '{role}', {domainSql}, 'active', 0, {Now})";

    private Task<long> ActiveUserAsync(string domain) =>
        InsertAsync(UserSql(RoleCodes.Employee, $"'{domain}'"));

    private Task<long> ClientAsync(string domain) =>
        InsertAsync($"INSERT INTO clients (domain_code, name, created_at) VALUES ('{domain}', 'Client', {Now})");

    private static string ProjectSql(string domain, long ownerId, string clientId, string billing, decimal revenue) =>
        "INSERT INTO projects (domain_code, title, status, billing_type, client_id, owner_id, start_date, revenue, revenue_currency, progress_pct, created_at) " +
        $"VALUES ('{domain}', 'Project', 'active', '{billing}', {clientId}, {ownerId}, '2026-09-01', {revenue}, 'USD', 0, {Now})";

    private static string WorkEntrySql(long userId, long projectId, string start, string end) =>
        "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
        $"VALUES ({userId}, {projectId}, 'Drawings', '{start}', '{end}', {Now})";

    private static string LabourWeekSql(long projectId, long userId, string weekStart) =>
        "INSERT INTO labour_weeks (project_id, week_start, submitted_at, submitted_by_id) " +
        $"VALUES ({projectId}, '{weekStart}', {Now}, {userId})";

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> InsertAsync(string sql)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql + "; SELECT LAST_INSERT_ID();", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ShouldViolateAsync(string sql, string constraintName)
    {
        var ex = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(sql));
        ex.Message.ShouldContain(constraintName);
    }
}
