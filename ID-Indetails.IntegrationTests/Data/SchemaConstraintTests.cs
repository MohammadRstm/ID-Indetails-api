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

        // a new company starts without teams; its super admin creates them
        (await context.Teams.CountAsync()).ShouldBe(0);
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

    // ---------- permission overrides ----------

    [Fact]
    public async Task Permission_override_is_a_grant_or_a_revoke_once_per_user_and_permission()
    {
        var userId = await ActiveUserAsync(DomainCodes.Design);
        string OverrideSql(string effect) =>
            "INSERT INTO user_permission_overrides (user_id, permission_code, effect, granted_by_id, created_at) " +
            $"VALUES ({userId}, '{PermissionCodes.ClientView}', '{effect}', {userId}, {Now})";

        await ShouldViolateAsync(OverrideSql("maybe"), "ck_user_permission_overrides_effect");
        await ExecuteAsync(OverrideSql("grant"));

        var ex = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(OverrideSql("revoke")));
        ex.Number.ShouldBe(1062); // one override per user and permission
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
    public async Task Pocket_money_goes_in_with_a_contribution_and_out_with_a_payout()
    {
        var owner = await ActiveUserAsync("execution");
        var site = await SiteProjectAsync(owner);
        string Entry(string kind, decimal amount) =>
            $"INSERT INTO pocket_entries (kind, project_id, amount, created_at, created_by_id) VALUES ('{kind}', {site}, {amount}, {Now}, {owner})";

        (await InsertAsync(Entry("contribution", 100))).ShouldBeGreaterThan(0);
        (await InsertAsync(Entry("payout", -40))).ShouldBeGreaterThan(0);
        await ShouldViolateAsync(Entry("contribution", -5), "ck_pocket_entries_amount_sign");
        await ShouldViolateAsync(Entry("payout", 5), "ck_pocket_entries_amount_sign");
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
        var project = await SiteProjectAsync(user);

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
        var project = await SiteProjectAsync(user);
        await InsertAsync(LabourWeekSql(project, user, "2026-09-14"));

        var ex = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(LabourWeekSql(project, user, "2026-09-14")));
        ex.Number.ShouldBe(1062);
    }

    [Fact]
    public async Task Site_expense_total_is_generated()
    {
        var user = await ActiveUserAsync("execution");
        var project = await SiteProjectAsync(user);

        var expense = await InsertAsync(
            "INSERT INTO site_expenses (project_id, name, quantity, unit, unit_cost, currency, spent_on, added_at, added_by_id) " +
            $"VALUES ({project}, 'Rebar', 2.5, 'tonne', 10.10, 'USD', '2026-09-28', {Now}, {user})");

        (await ScalarAsync<decimal>($"SELECT total FROM site_expenses WHERE id = {expense}")).ShouldBe(25.25m);
    }

    [Fact]
    public async Task A_site_expense_is_materials_unless_it_says_otherwise_and_has_a_known_category()
    {
        var user = await ActiveUserAsync("execution");
        var project = await SiteProjectAsync(user);

        var plain = await InsertAsync(SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "10"));
        (await ScalarAsync<string>($"SELECT category FROM site_expenses WHERE id = {plain}")).ShouldBe("materials");

        foreach (var category in new[] { "materials", "contractor", "equipment", "fees", "other" })
        {
            await InsertAsync(SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "10", category: category));
        }

        await ShouldViolateAsync(SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "10", category: "luxury"), "ck_site_expenses_category");
    }

    [Fact]
    public async Task A_site_expense_label_must_be_an_account_label_and_a_used_label_cannot_be_deleted()
    {
        var user = await ActiveUserAsync("execution");
        var project = await SiteProjectAsync(user);
        var label = await InsertAsync($"INSERT INTO account_labels (name, position, created_at) VALUES ('Schema {Guid.NewGuid():N}', 99, {Now})");

        var unknown = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(
            SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "10", accountLabelId: "999999999")));
        unknown.Number.ShouldBe(1452); // no such parent row

        await InsertAsync(SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "10", accountLabelId: label.ToString()));
        var used = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync($"DELETE FROM account_labels WHERE id = {label}"));
        used.Number.ShouldBe(1451); // row is referenced by a foreign key
    }

    [Fact]
    public async Task A_payment_label_must_be_an_account_label_and_a_used_label_cannot_be_deleted()
    {
        var owner = await ActiveUserAsync("execution");
        var client = await ClientAsync("execution");
        var site = await SiteProjectAsync(owner);
        var label = await InsertAsync($"INSERT INTO account_labels (name, position, created_at) VALUES ('Schema {Guid.NewGuid():N}', 98, {Now})");
        string Payment(string accountLabelId) =>
            "INSERT INTO payments (project_id, client_id, account_label_id, amount, currency, fx_rate, amount_usd, received_on, method, recorded_at, is_initial) " +
            $"VALUES ({site}, {client}, {accountLabelId}, 1000, 'USD', 1, 1000, '2026-09-28', 'bank', {Now}, 0)";

        var unknown = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(Payment("999999999")));
        unknown.Number.ShouldBe(1452); // no such parent row

        await InsertAsync(Payment(label.ToString()));
        await InsertAsync(Payment("NULL"));
        var used = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync($"DELETE FROM account_labels WHERE id = {label}"));
        used.Number.ShouldBe(1451); // row is referenced by a foreign key
    }

    [Fact]
    public async Task Supervision_taken_is_a_positive_amount_of_a_site()
    {
        var owner = await ActiveUserAsync("execution");
        var site = await SiteProjectAsync(owner);
        string Withdrawal(string amount) =>
            "INSERT INTO supervision_withdrawals (project_id, amount_usd, taken_on, created_by_id, created_at) " +
            $"VALUES ({site}, {amount}, '2026-09-28', {owner}, {Now})";

        (await InsertAsync(Withdrawal("1500.50"))).ShouldBeGreaterThan(0);
        await ShouldViolateAsync(Withdrawal("0"), "ck_supervision_withdrawals_amount_positive");
        await ShouldViolateAsync(Withdrawal("-10"), "ck_supervision_withdrawals_amount_positive");

        var project = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(
            "INSERT INTO supervision_withdrawals (project_id, amount_usd, taken_on, created_by_id, created_at) " +
            $"VALUES (999999999, 10, '2026-09-28', {owner}, {Now})"));
        project.Number.ShouldBe(1452); // no such parent row
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
            "INSERT INTO company_settings (id, name, time_zone, week_start, use_24_hour_time, default_currency) " +
            "VALUES (2, 'Second company', 'Asia/Beirut', 1, 0, 'USD')",
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

    // ---------- Execution money (ExecutionPricingAndWorkingTime) ----------

    [Fact]
    public async Task Execution_project_must_be_client_work()
    {
        var owner = await ActiveUserAsync("execution");

        await ShouldViolateAsync(
            ProjectSql("execution", owner, "NULL", "internal", 0),
            "ck_projects_execution_is_client_work");
    }

    [Fact]
    public async Task A_work_price_exists_exactly_on_execution_projects()
    {
        var siteOwner = await ActiveUserAsync("execution");
        var siteClient = await ClientAsync("execution");
        await ShouldViolateAsync(
            ProjectSql("execution", siteOwner, siteClient.ToString(), "client", 0, projectFee: "NULL"),
            "ck_projects_execution_money_fields");

        var designOwner = await ActiveUserAsync("design");
        await ShouldViolateAsync(
            ProjectSql("design", designOwner, "NULL", "internal", 0, projectFee: "500"),
            "ck_projects_execution_money_fields");
    }

    [Fact]
    public async Task Supervision_is_a_fraction_of_the_work_price()
    {
        var owner = await ActiveUserAsync("execution");
        var client = await ClientAsync("execution");
        string Site(string rate) =>
            "INSERT INTO projects (domain_code, title, status, billing_type, client_id, owner_id, start_date, revenue, revenue_currency, progress_pct, project_fee, supervision_rate, created_at) " +
            $"VALUES ('execution', 'Project', 'active', 'client', {client}, {owner}, '2026-09-01', 0, 'USD', 0, 0, {rate}, {Now})";

        await InsertAsync(Site("0.10"));
        await ShouldViolateAsync(Site("1.5"), "ck_projects_supervision_rate_range");
    }

    [Fact]
    public async Task A_site_expense_needs_quantity_and_unit_cost_or_a_lump_sum()
    {
        var user = await ActiveUserAsync("execution");
        var project = await SiteProjectAsync(user);

        await ShouldViolateAsync(SiteExpenseSql(project, user, quantity: "3", unitCost: "NULL", amount: "NULL"), "ck_site_expenses_has_cost");

        var lumpSum = await InsertAsync(SiteExpenseSql(project, user, quantity: "NULL", unitCost: "NULL", amount: "1250.00"));
        (await ScalarAsync<decimal>($"SELECT total FROM site_expenses WHERE id = {lumpSum}")).ShouldBe(1250.00m);

        // Unit cost keeps 4 decimals (derived as total ÷ quantity); the total is rounded to cents.
        var counted = await InsertAsync(SiteExpenseSql(project, user, quantity: "3", unitCost: "33.3333", amount: "NULL"));
        (await ScalarAsync<decimal>($"SELECT unit_cost FROM site_expenses WHERE id = {counted}")).ShouldBe(33.3333m);
        (await ScalarAsync<decimal>($"SELECT total FROM site_expenses WHERE id = {counted}")).ShouldBe(100.00m);
    }

    [Fact]
    public async Task Sunday_is_never_a_labour_day()
    {
        var user = await ActiveUserAsync("execution");
        var project = await SiteProjectAsync(user);
        var week = await InsertAsync(LabourWeekSql(project, user, "2026-09-21"));

        // 2026-09-27 is the Sunday of that week.
        await ShouldViolateAsync(
            $"INSERT INTO labour_week_days (labour_week_id, work_date, workers, daily_pay) VALUES ({week}, '2026-09-27', 5, 40)",
            "ck_labour_week_days_not_sunday");
    }

    [Fact]
    public async Task One_live_initial_payment_per_project_and_voiding_frees_the_slot()
    {
        var user = await ActiveUserAsync("execution");
        var client = await ClientAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, client.ToString(), "client", 0));

        var first = await InsertAsync(PaymentSql(project, client, isInitial: true));
        var duplicate = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(PaymentSql(project, client, isInitial: true)));
        duplicate.Number.ShouldBe(1062);

        // Ordinary payments are unaffected, and a voided initial payment can be replaced.
        (await InsertAsync(PaymentSql(project, client, isInitial: false))).ShouldBeGreaterThan(0);
        await ExecuteAsync($"UPDATE payments SET voided_at = {Now}, void_reason = 'wrong amount' WHERE id = {first}");
        (await InsertAsync(PaymentSql(project, client, isInitial: true))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_boq_is_numbered_unless_it_is_internal_and_only_a_vo_has_a_reason_or_a_negative_total()
    {
        var user = await ActiveUserAsync("execution");
        var client = await ClientAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, client.ToString(), "client", 0));
        string Number() => $"'BQ-2026-{Random.Shared.Next(100000, 999999)}'";

        (await InsertAsync(BoqSql(project, user, "internal", "NULL"))).ShouldBeGreaterThan(0);
        var numbered = await InsertAsync(BoqSql(project, user, "client", Number()));
        await InsertAsync(BoqSql(project, user, "vo", "'VO-2026-" + Random.Shared.Next(100000, 999999) + "'", reason: "'Bigger kitchen'", total: "-500"));

        await ShouldViolateAsync(BoqSql(project, user, "client", "NULL"), "ck_boqs_number_by_kind");
        await ShouldViolateAsync(BoqSql(project, user, "internal", Number()), "ck_boqs_number_by_kind");
        await ShouldViolateAsync(BoqSql(project, user, "internal", "NULL", status: "agreed"), "ck_boqs_internal_is_draft");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), reason: "'Why'"), "ck_boqs_reason_only_vo");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), total: "-1"), "ck_boqs_amounts_by_kind");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), adjustment: "'total'", adjustmentValue: "-5"), "ck_boqs_amounts_by_kind");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), adjustment: "'percent'", adjustmentValue: "-1"), "ck_boqs_adjustment_percent_range");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), adjustment: "'percent'", adjustmentValue: "1.5"), "ck_boqs_adjustment_percent_range");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), adjustmentValue: "0.1"), "ck_boqs_adjustment_value");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), adjustment: "'percent'"), "ck_boqs_adjustment_value");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), status: "agreed", agreedAt: "NULL"), "ck_boqs_agreed");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), status: "draft", agreedAt: Now), "ck_boqs_agreed");
        await ShouldViolateAsync(BoqSql(project, user, "quote", Number()), "ck_boqs_kind");
        await ShouldViolateAsync(BoqSql(project, user, "client", Number(), status: "withdrawn"), "ck_boqs_status");
        (await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(
            $"INSERT INTO boqs (project_id, kind, status, number, title, author_id, adjustment_type, subtotal_usd, total_usd, created_at, updated_at) " +
            $"SELECT project_id, kind, status, number, title, author_id, adjustment_type, subtotal_usd, total_usd, created_at, updated_at FROM boqs WHERE id = {numbered}")))
            .Number.ShouldBe(1062);

        // a line: no negative quantity, waste between none and all of it (a negative rate is the BOQ's rule)
        (await InsertAsync(BoqLineSql(numbered, quantity: "10", waste: "0.05", rate: "-3"))).ShouldBeGreaterThan(0);
        await ShouldViolateAsync(BoqLineSql(numbered, quantity: "-1", waste: "0", rate: "3"), "ck_boq_lines_quantity_non_negative");
        await ShouldViolateAsync(BoqLineSql(numbered, quantity: "1", waste: "1.5", rate: "3"), "ck_boq_lines_waste_rate_range");
        await ShouldViolateAsync(BoqLineSql(numbered, quantity: "1", waste: "-0.1", rate: "3"), "ck_boq_lines_waste_rate_range");

        await ShouldViolateAsync(
            $"INSERT INTO boq_deliveries (boq_id, channel, total_usd, sent_at) VALUES ({numbered}, 'pigeon', 100, {Now})", "ck_boq_deliveries_channel");
        (await InsertAsync($"INSERT INTO boq_deliveries (boq_id, channel, total_usd, sent_at) VALUES ({numbered}, 'email', 100, {Now})")).ShouldBeGreaterThan(0);
        await ShouldViolateAsync("INSERT INTO boq_sequences (prefix, year, last_number) VALUES ('ZZ', 2026, -1)", "ck_boq_sequences_number_non_negative");
    }

    [Fact]
    public async Task A_blocked_change_can_be_a_boq_agreement_or_withdrawal_and_nothing_else_new()
    {
        var user = await ActiveUserAsync("execution");
        var client = await ClientAsync("execution");
        var project = await InsertAsync(ProjectSql("execution", user, client.ToString(), "client", 0));
        string Change(string kind) =>
            "INSERT INTO blocked_changes (project_id, kind, payload, requested_by_id, requested_at, status) " +
            $"VALUES ({project}, '{kind}', '{{}}', {user}, {Now}, 'pending')";

        (await InsertAsync(Change("boq_agree"))).ShouldBeGreaterThan(0);
        (await InsertAsync(Change("boq_withdraw"))).ShouldBeGreaterThan(0);
        await ShouldViolateAsync(Change("boq_delete"), "ck_blocked_changes_kind");
    }

    // ---------- working time (ExecutionPricingAndWorkingTime) ----------

    [Fact]
    public async Task Default_working_week_is_seeded_as_monday_to_friday_nine_to_five_with_a_paid_hour_break()
    {
        (await ScalarAsync<string>(
                "SELECT CONCAT(work_start_minute, '-', work_end_minute, '/', work_days, '/', break_minutes, '/', overtime_multiplier) " +
                "FROM working_time_policies WHERE effective_from = '2000-01-01'"))
            .ShouldBe("540-1020/62/60/1.00");
    }

    [Fact]
    public async Task Working_time_policy_rejects_impossible_hours_and_breaks()
    {
        // Each case breaks exactly one rule, so MySQL reports that rule's constraint.
        await ShouldViolateAsync(PolicySql("2030-01-01", start: 540, end: 1500, days: 62, breakMinutes: 60), "ck_working_time_policies_hours");
        await ShouldViolateAsync(PolicySql("2030-01-02", start: 540, end: 1020, days: 0, breakMinutes: 60), "ck_working_time_policies_work_days");
        await ShouldViolateAsync(PolicySql("2030-01-03", start: 540, end: 600, days: 62, breakMinutes: 60), "ck_working_time_policies_break");

        // Only one policy per effective date.
        var ex = await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(PolicySql("2000-01-01", 540, 1020, 62, 60)));
        ex.Number.ShouldBe(1062);
    }

    [Fact]
    public async Task Personal_schedule_is_dated_and_days_are_single_spans()
    {
        var user = await ActiveUserAsync("design");
        var schedule = await InsertAsync(
            $"INSERT INTO user_schedules (user_id, effective_from, follows_default, created_at) VALUES ({user}, '2026-10-01', 0, {Now})");

        (await Should.ThrowAsync<MySqlException>(() => ExecuteAsync(
                $"INSERT INTO user_schedules (user_id, effective_from, follows_default, created_at) VALUES ({user}, '2026-10-01', 1, {Now})")))
            .Number.ShouldBe(1062);

        await ShouldViolateAsync(ScheduleDaySql(schedule, weekday: 7, start: 540, end: 780), "ck_user_schedule_days_weekday");
        await ShouldViolateAsync(ScheduleDaySql(schedule, weekday: 1, start: 780, end: 540), "ck_user_schedule_days_hours");
        await ExecuteAsync(ScheduleDaySql(schedule, weekday: 1, start: 540, end: 780));
    }

    [Fact]
    public async Task Hour_excuse_is_for_a_month_and_minutes_go_with_the_minutes_kind()
    {
        var user = await ActiveUserAsync("design");

        await ShouldViolateAsync(ExcuseSql(user, "2026-09-15", "whole_month", "NULL"), "ck_hour_excuses_month_start");
        await ShouldViolateAsync(ExcuseSql(user, "2026-09-01", "minutes", "NULL"), "ck_hour_excuses_minutes");
        await ShouldViolateAsync(ExcuseSql(user, "2026-09-01", "days", "120"), "ck_hour_excuses_minutes");
        await ShouldViolateAsync(ExcuseSql(user, "2026-09-01", "sick", "NULL"), "ck_hour_excuses_kind");

        (await InsertAsync(ExcuseSql(user, "2026-09-01", "minutes", "90"))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Obsolete_cost_plus_price_permission_is_gone()
    {
        (await ScalarAsync<int>("SELECT COUNT(*) FROM permissions WHERE code = 'project:set_price'")).ShouldBe(0);
        (await ScalarAsync<int>("SELECT COUNT(*) FROM permissions WHERE code = 'project:pricing_manage'")).ShouldBe(1);
    }

    // ---------- client portal (PortalUx) ----------

    [Fact]
    public async Task Only_a_deleted_portal_message_has_no_text()
    {
        var user = await ActiveUserAsync("design");
        var project = await InsertAsync(ProjectSql("design", user, "NULL", "internal", 0));

        await ShouldViolateAsync(PortalMessageSql(project, user, "NULL"), "ck_portal_messages_body_unless_deleted");
        (await InsertAsync(PortalMessageSql(project, user, Now))).ShouldBeGreaterThan(0);
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

    /// <summary>Execution projects get the work price they require; Design projects get none.</summary>
    private static string ProjectSql(string domain, long ownerId, string clientId, string billing, decimal revenue) =>
        domain == "execution"
            ? ProjectSql(domain, ownerId, clientId, billing, revenue, projectFee: "0")
            : ProjectSql(domain, ownerId, clientId, billing, revenue, projectFee: "NULL");

    private static string ProjectSql(
        string domain, long ownerId, string clientId, string billing, decimal revenue, string projectFee) =>
        "INSERT INTO projects (domain_code, title, status, billing_type, client_id, owner_id, start_date, revenue, revenue_currency, progress_pct, project_fee, created_at) " +
        $"VALUES ('{domain}', 'Project', 'active', '{billing}', {clientId}, {ownerId}, '2026-09-01', {revenue}, 'USD', 0, {projectFee}, {Now})";

    /// <summary>An Execution site project: always client work, with a work price.</summary>
    private async Task<long> SiteProjectAsync(long ownerId)
    {
        var client = await ClientAsync("execution");
        return await InsertAsync(ProjectSql("execution", ownerId, client.ToString(), "client", 0));
    }

    private static string WorkEntrySql(long userId, long projectId, string start, string end) =>
        "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
        $"VALUES ({userId}, {projectId}, 'Drawings', '{start}', '{end}', {Now})";

    private static string SiteExpenseSql(
        long projectId, long userId, string quantity, string unitCost, string amount, string? category = null, string accountLabelId = "NULL") =>
        category is null
            ? "INSERT INTO site_expenses (project_id, name, quantity, unit_cost, amount, currency, spent_on, account_label_id, added_at, added_by_id) " +
              $"VALUES ({projectId}, 'Concrete', {quantity}, {unitCost}, {amount}, 'USD', '2026-09-28', {accountLabelId}, {Now}, {userId})"
            : "INSERT INTO site_expenses (project_id, name, quantity, unit_cost, amount, currency, spent_on, category, account_label_id, added_at, added_by_id) " +
              $"VALUES ({projectId}, 'Concrete', {quantity}, {unitCost}, {amount}, 'USD', '2026-09-28', '{category}', {accountLabelId}, {Now}, {userId})";

    private static string PaymentSql(long projectId, long clientId, bool isInitial) =>
        "INSERT INTO payments (project_id, client_id, amount, currency, fx_rate, amount_usd, received_on, method, recorded_at, is_initial) " +
        $"VALUES ({projectId}, {clientId}, 1000, 'USD', 1, 1000, '2026-09-28', 'bank', {Now}, {(isInitial ? 1 : 0)})";

    private static string BoqSql(
        long projectId, long authorId, string kind, string number, string status = "draft", string reason = "NULL", string total = "100",
        string adjustment = "'none'", string adjustmentValue = "NULL", string? agreedAt = null) =>
        "INSERT INTO boqs (project_id, kind, status, number, title, reason, author_id, adjustment_type, adjustment_value, subtotal_usd, total_usd, created_at, updated_at, agreed_at) " +
        $"VALUES ({projectId}, '{kind}', '{status}', {number}, 'Villa', {reason}, {authorId}, {adjustment}, {adjustmentValue}, {total}, {total}, {Now}, {Now}, {agreedAt ?? (status == "agreed" ? Now : "NULL")})";

    private static string BoqLineSql(long boqId, string quantity, string waste, string rate) =>
        "INSERT INTO boq_lines (boq_id, position, description, quantity, waste_rate, rate) " +
        $"VALUES ({boqId}, 1, 'Concrete', {quantity}, {waste}, {rate})";

    private static string PolicySql(string effectiveFrom, int start, int end, int days, int breakMinutes) =>
        "INSERT INTO working_time_policies (effective_from, work_start_minute, work_end_minute, work_days, break_minutes, overtime_multiplier, created_at) " +
        $"VALUES ('{effectiveFrom}', {start}, {end}, {days}, {breakMinutes}, 1, {Now})";

    private static string ScheduleDaySql(long scheduleId, int weekday, int start, int end) =>
        $"INSERT INTO user_schedule_days (schedule_id, weekday, start_minute, end_minute) VALUES ({scheduleId}, {weekday}, {start}, {end})";

    private static string ExcuseSql(long userId, string monthStart, string kind, string minutes) =>
        "INSERT INTO hour_excuses (user_id, month_start, kind, minutes, is_paid, note, created_at) " +
        $"VALUES ({userId}, '{monthStart}', '{kind}', {minutes}, 1, 'Annual leave', {Now})";

    private static string LabourWeekSql(long projectId, long userId, string weekStart) =>
        "INSERT INTO labour_weeks (project_id, week_start, submitted_at, submitted_by_id) " +
        $"VALUES ({projectId}, '{weekStart}', {Now}, {userId})";

    /// <summary>A staff member's chat message without text, deleted at <paramref name="deletedAt"/> (SQL).</summary>
    private static string PortalMessageSql(long projectId, long userId, string deletedAt) =>
        "INSERT INTO portal_messages (project_id, author_user_id, body, client_token, created_at, deleted_at) " +
        $"VALUES ({projectId}, {userId}, NULL, '{Guid.NewGuid():N}', {Now}, {deletedAt})";

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
