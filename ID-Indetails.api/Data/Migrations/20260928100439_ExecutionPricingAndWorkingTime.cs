using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionPricingAndWorkingTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_markup_non_negative",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_quantity_positive",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_unit_cost_non_negative",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_company_settings_overtime_multiplier",
                table: "company_settings");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project:set_price", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project:set_price", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "project:set_price");

            migrationBuilder.DropColumn(
                name: "price_set_at",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "category",
                table: "project_materials");

            migrationBuilder.DropColumn(
                name: "overtime_multiplier",
                table: "company_settings");

            migrationBuilder.RenameColumn(
                name: "markup",
                table: "projects",
                newName: "material_markup");

            migrationBuilder.AddColumn<decimal>(
                name: "project_fee",
                table: "projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "project_meetings",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "updated_by_id",
                table: "project_meetings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "project_materials",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "unit",
                table: "project_materials",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "project_materials",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(14,3)",
                oldPrecision: 14,
                oldScale: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                table: "project_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "client_price",
                table: "project_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_initial",
                table: "payments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "project_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                computedColumnSql: "CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComputedColumnSql: "ROUND(`quantity` * `unit_cost`, 2)",
                oldStored: true);

            migrationBuilder.AddColumn<long>(
                name: "initial_for_project_id",
                table: "payments",
                type: "bigint",
                nullable: true,
                computedColumnSql: "IF(`is_initial` = 1 AND `voided_at` IS NULL, `project_id`, NULL)",
                stored: true);

            migrationBuilder.CreateTable(
                name: "client_statements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    number = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    includes_earlier = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    since = table.Column<DateOnly>(type: "date", nullable: true),
                    received_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    agreed_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_statements", x => x.id);
                    table.CheckConstraint("ck_client_statements_amounts_non_negative", "`received_usd` >= 0 AND `agreed_usd` >= 0");
                    table.CheckConstraint("ck_client_statements_mode", "`mode` IN ('detail', 'total')");
                    table.ForeignKey(
                        name: "fk_client_statements_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_statements_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_statements_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hour_excuses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    month_start = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    minutes = table.Column<int>(type: "int", nullable: true),
                    is_paid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    note = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    deleted_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hour_excuses", x => x.id);
                    table.CheckConstraint("ck_hour_excuses_kind", "`kind` IN ('minutes', 'days', 'whole_month')");
                    table.CheckConstraint("ck_hour_excuses_minutes", "(`kind` = 'minutes') = (`minutes` IS NOT NULL) AND (`minutes` IS NULL OR `minutes` > 0)");
                    table.CheckConstraint("ck_hour_excuses_month_start", "DAYOFMONTH(`month_start`) = 1");
                    table.ForeignKey(
                        name: "fk_hour_excuses_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hour_excuses_users_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hour_excuses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "statement_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "int", nullable: false),
                    last_number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_sequences", x => x.year);
                    table.CheckConstraint("ck_statement_sequences_number_non_negative", "`last_number` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_schedules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    follows_default = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_schedules_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_schedules_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "working_time_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    work_start_minute = table.Column<short>(type: "smallint", nullable: false),
                    work_end_minute = table.Column<short>(type: "smallint", nullable: false),
                    work_days = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    break_minutes = table.Column<short>(type: "smallint", nullable: false),
                    overtime_multiplier = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_time_policies", x => x.id);
                    table.CheckConstraint("ck_working_time_policies_break", "`break_minutes` >= 0 AND `break_minutes` < `work_end_minute` - `work_start_minute`");
                    table.CheckConstraint("ck_working_time_policies_hours", "`work_start_minute` >= 0 AND `work_end_minute` <= 1440 AND `work_start_minute` < `work_end_minute`");
                    table.CheckConstraint("ck_working_time_policies_overtime_multiplier", "`overtime_multiplier` >= 1");
                    table.CheckConstraint("ck_working_time_policies_work_days", "`work_days` BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "fk_working_time_policies_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_statement_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    statement_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_statement_deliveries", x => x.id);
                    table.CheckConstraint("ck_client_statement_deliveries_channel", "`channel` IN ('email', 'whatsapp', 'download')");
                    table.ForeignKey(
                        name: "fk_client_statement_deliveries_client_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "client_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_statement_deliveries_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_statement_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    statement_id = table.Column<long>(type: "bigint", nullable: false),
                    material_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    is_earlier = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_listed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_statement_lines", x => x.id);
                    table.CheckConstraint("ck_client_statement_lines_price_non_negative", "`price` >= 0");
                    table.ForeignKey(
                        name: "fk_client_statement_lines_client_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "client_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_statement_lines_project_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "project_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hour_excuse_days",
                columns: table => new
                {
                    excuse_id = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hour_excuse_days", x => new { x.excuse_id, x.day });
                    table.ForeignKey(
                        name: "fk_hour_excuse_days_hour_excuses_excuse_id",
                        column: x => x.excuse_id,
                        principalTable: "hour_excuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_schedule_days",
                columns: table => new
                {
                    schedule_id = table.Column<long>(type: "bigint", nullable: false),
                    weekday = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    start_minute = table.Column<short>(type: "smallint", nullable: false),
                    end_minute = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_schedule_days", x => new { x.schedule_id, x.weekday });
                    table.CheckConstraint("ck_user_schedule_days_hours", "`start_minute` >= 0 AND `end_minute` <= 1440 AND `start_minute` < `end_minute`");
                    table.CheckConstraint("ck_user_schedule_days_weekday", "`weekday` BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_user_schedule_days_user_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "user_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "hours:excuse", "Excuse hours and days off in the hours ledger", null },
                    { "project:pricing_manage", "Set the project fee, default material markup and material client prices", "execution" },
                    { "schedule:manage", "Set personal work schedules", null },
                    { "statement:create", "Create client statements of materials supplied", "execution" },
                    { "statement:send", "Send client statements", "execution" },
                    { "working_time:manage", "Set the company working week, break and overtime multiplier", null }
                });

            migrationBuilder.InsertData(
                table: "working_time_policies",
                columns: new[] { "id", "break_minutes", "created_at", "created_by_id", "effective_from", "overtime_multiplier", "work_days", "work_end_minute", "work_start_minute" },
                values: new object[] { 1L, (short)60, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateOnly(2000, 1, 1), 1m, (byte)62, (short)1020, (short)540 });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "project:pricing_manage", "admin" },
                    { "statement:create", "admin" },
                    { "statement:send", "admin" },
                    { "hours:excuse", "super_admin" },
                    { "project:pricing_manage", "super_admin" },
                    { "schedule:manage", "super_admin" },
                    { "statement:create", "super_admin" },
                    { "statement:send", "super_admin" },
                    { "working_time:manage", "super_admin" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_is_client_work",
                table: "projects",
                sql: "`domain_code` <> 'execution' OR `billing_type` = 'client'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects",
                sql: "(`domain_code` = 'execution' AND `project_fee` IS NOT NULL AND `material_markup` IS NOT NULL) OR (`domain_code` <> 'execution' AND `project_fee` IS NULL AND `material_markup` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_material_markup_range",
                table: "projects",
                sql: "`material_markup` IS NULL OR `material_markup` BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_project_fee_non_negative",
                table: "projects",
                sql: "`project_fee` IS NULL OR `project_fee` >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_project_meetings_updated_by_id",
                table: "project_meetings",
                column: "updated_by_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_amount_positive",
                table: "project_materials",
                sql: "`amount` IS NULL OR `amount` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_client_price_non_negative",
                table: "project_materials",
                sql: "`client_price` IS NULL OR `client_price` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_has_cost",
                table: "project_materials",
                sql: "(COALESCE(`quantity`, 0) > 0 AND COALESCE(`unit_cost`, 0) > 0) OR COALESCE(`amount`, 0) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_quantity_positive",
                table: "project_materials",
                sql: "`quantity` IS NULL OR `quantity` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_unit_cost_non_negative",
                table: "project_materials",
                sql: "`unit_cost` IS NULL OR `unit_cost` >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_payments_initial_for_project_id",
                table: "payments",
                column: "initial_for_project_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_labour_week_days_not_sunday",
                table: "labour_week_days",
                sql: "WEEKDAY(`work_date`) <> 6");

            migrationBuilder.CreateIndex(
                name: "ix_client_statement_deliveries_sent_by_id",
                table: "client_statement_deliveries",
                column: "sent_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_statement_deliveries_statement_id",
                table: "client_statement_deliveries",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_statement_lines_material_id",
                table: "client_statement_lines",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_statement_lines_statement_id_material_id",
                table: "client_statement_lines",
                columns: new[] { "statement_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_statement_lines_statement_id_position",
                table: "client_statement_lines",
                columns: new[] { "statement_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_statements_client_id",
                table: "client_statements",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_statements_created_by_id",
                table: "client_statements",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_statements_number",
                table: "client_statements",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_statements_project_id",
                table: "client_statements",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_hour_excuses_created_by_id",
                table: "hour_excuses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_hour_excuses_deleted_by_id",
                table: "hour_excuses",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_hour_excuses_user_id_month_start",
                table: "hour_excuses",
                columns: new[] { "user_id", "month_start" });

            migrationBuilder.CreateIndex(
                name: "ix_user_schedules_created_by_id",
                table: "user_schedules",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_schedules_user_id_effective_from",
                table: "user_schedules",
                columns: new[] { "user_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_working_time_policies_created_by_id",
                table: "working_time_policies",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_working_time_policies_effective_from",
                table: "working_time_policies",
                column: "effective_from",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_project_meetings_users_updated_by_id",
                table: "project_meetings",
                column: "updated_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_meetings_users_updated_by_id",
                table: "project_meetings");

            migrationBuilder.DropTable(
                name: "client_statement_deliveries");

            migrationBuilder.DropTable(
                name: "client_statement_lines");

            migrationBuilder.DropTable(
                name: "hour_excuse_days");

            migrationBuilder.DropTable(
                name: "statement_sequences");

            migrationBuilder.DropTable(
                name: "user_schedule_days");

            migrationBuilder.DropTable(
                name: "working_time_policies");

            migrationBuilder.DropTable(
                name: "client_statements");

            migrationBuilder.DropTable(
                name: "hour_excuses");

            migrationBuilder.DropTable(
                name: "user_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_is_client_work",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_material_markup_range",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_project_fee_non_negative",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_project_meetings_updated_by_id",
                table: "project_meetings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_amount_positive",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_client_price_non_negative",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_has_cost",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_quantity_positive",
                table: "project_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_materials_unit_cost_non_negative",
                table: "project_materials");

            migrationBuilder.DropIndex(
                name: "ix_payments_initial_for_project_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_labour_week_days_not_sunday",
                table: "labour_week_days");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project:pricing_manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "statement:create", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "statement:send", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "hours:excuse", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project:pricing_manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "schedule:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "statement:create", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "statement:send", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "working_time:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "hours:excuse");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "project:pricing_manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "schedule:manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "statement:create");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "statement:send");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "working_time:manage");

            migrationBuilder.DropColumn(
                name: "initial_for_project_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "project_fee",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "project_meetings");

            migrationBuilder.DropColumn(
                name: "updated_by_id",
                table: "project_meetings");

            migrationBuilder.DropColumn(
                name: "amount",
                table: "project_materials");

            migrationBuilder.DropColumn(
                name: "client_price",
                table: "project_materials");

            migrationBuilder.DropColumn(
                name: "is_initial",
                table: "payments");

            migrationBuilder.RenameColumn(
                name: "material_markup",
                table: "projects",
                newName: "markup");

            migrationBuilder.AddColumn<DateTime>(
                name: "price_set_at",
                table: "projects",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "project_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "project_materials",
                keyColumn: "unit",
                keyValue: null,
                column: "unit",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "unit",
                table: "project_materials",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "project_materials",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(14,3)",
                oldPrecision: 14,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "project_materials",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "overtime_multiplier",
                table: "company_settings",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "project_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                computedColumnSql: "ROUND(`quantity` * `unit_cost`, 2)",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComputedColumnSql: "CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END",
                oldStored: true);

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "project:set_price", "Set the cost-plus price of an Execution project", "execution" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "project:set_price", "admin" },
                    { "project:set_price", "super_admin" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_markup_non_negative",
                table: "projects",
                sql: "`markup` IS NULL OR `markup` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_quantity_positive",
                table: "project_materials",
                sql: "`quantity` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_materials_unit_cost_non_negative",
                table: "project_materials",
                sql: "`unit_cost` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_company_settings_overtime_multiplier",
                table: "company_settings",
                sql: "`overtime_multiplier` >= 1");
        }
    }
}
