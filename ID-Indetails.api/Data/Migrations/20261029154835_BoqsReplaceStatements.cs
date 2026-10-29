using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoqsReplaceStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Permissions: statement:create becomes boq:create with its grants (the role's and each person's), and
            // statement:send goes. The new permission is made first, so nothing that points at the old one is left without.
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "boq:create", "Write internal draft BOQs of a site", "execution" });

            migrationBuilder.Sql(
                "INSERT INTO role_permissions (permission_code, role_code) " +
                "SELECT 'boq:create', role_code FROM role_permissions WHERE permission_code = 'statement:create';");
            migrationBuilder.Sql("UPDATE user_permission_overrides SET permission_code = 'boq:create' WHERE permission_code = 'statement:create';");
            migrationBuilder.Sql("DELETE FROM user_permission_overrides WHERE permission_code = 'statement:send';");
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission_code IN ('statement:create', 'statement:send');");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "statement:create");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "statement:send");

            // Rows that record statements go too: nothing is in production, and they would point at records that no longer exist.
            migrationBuilder.Sql("DELETE FROM notifications WHERE kind = 'statement_sent';");
            migrationBuilder.Sql("DELETE FROM activity_events WHERE kind IN ('statement_created', 'statement_sent');");

            migrationBuilder.DropTable(
                name: "client_statement_deliveries");

            migrationBuilder.DropTable(
                name: "client_statement_lines");

            migrationBuilder.DropTable(
                name: "statement_sequences");

            migrationBuilder.DropTable(
                name: "client_statements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddColumn<decimal>(
                name: "previous_supervision_rate",
                table: "project_payment_changes",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "supervision_rate",
                table: "project_payment_changes",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "boq_sequences",
                columns: table => new
                {
                    prefix = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    year = table.Column<int>(type: "int", nullable: false),
                    last_number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boq_sequences", x => new { x.prefix, x.year });
                    table.CheckConstraint("ck_boq_sequences_number_non_negative", "`last_number` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "boqs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    adjustment_value = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: true),
                    subtotal_usd = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    total_usd = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    agreed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    agreed_by_id = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boqs", x => x.id);
                    table.CheckConstraint("ck_boqs_adjustment_percent_range", "`adjustment_type` <> 'percent' OR `adjustment_value` BETWEEN -0.99 AND 1");
                    table.CheckConstraint("ck_boqs_adjustment_type", "`adjustment_type` IN ('none', 'percent', 'total')");
                    table.CheckConstraint("ck_boqs_adjustment_value", "(`adjustment_type` = 'none') = (`adjustment_value` IS NULL)");
                    table.CheckConstraint("ck_boqs_agreed", "(`status` = 'agreed') = (`agreed_at` IS NOT NULL)");
                    table.CheckConstraint("ck_boqs_amounts_by_kind", "`kind` = 'vo' OR (`subtotal_usd` >= 0 AND `total_usd` >= 0 AND (`adjustment_type` <> 'total' OR `adjustment_value` >= 0))");
                    table.CheckConstraint("ck_boqs_internal_is_draft", "`kind` <> 'internal' OR `status` = 'draft'");
                    table.CheckConstraint("ck_boqs_kind", "`kind` IN ('internal', 'client', 'vo')");
                    table.CheckConstraint("ck_boqs_number_by_kind", "(`kind` = 'internal') = (`number` IS NULL)");
                    table.CheckConstraint("ck_boqs_reason_only_vo", "`reason` IS NULL OR `kind` = 'vo'");
                    table.CheckConstraint("ck_boqs_status", "`status` IN ('draft', 'agreed')");
                    table.ForeignKey(
                        name: "fk_boqs_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_boqs_users_agreed_by_id",
                        column: x => x.agreed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_boqs_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "boq_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    boq_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total_usd = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boq_deliveries", x => x.id);
                    table.CheckConstraint("ck_boq_deliveries_channel", "`channel` IN ('email', 'whatsapp', 'download')");
                    table.ForeignKey(
                        name: "fk_boq_deliveries_boqs_boq_id",
                        column: x => x.boq_id,
                        principalTable: "boqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_boq_deliveries_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "boq_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    boq_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    waste_rate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rate = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boq_lines", x => x.id);
                    table.CheckConstraint("ck_boq_lines_quantity_non_negative", "`quantity` >= 0");
                    table.CheckConstraint("ck_boq_lines_waste_rate_range", "`waste_rate` BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "fk_boq_lines_boqs_boq_id",
                        column: x => x.boq_id,
                        principalTable: "boqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw')");

            migrationBuilder.CreateIndex(
                name: "ix_boq_deliveries_boq_id",
                table: "boq_deliveries",
                column: "boq_id");

            migrationBuilder.CreateIndex(
                name: "ix_boq_deliveries_sent_by_id",
                table: "boq_deliveries",
                column: "sent_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_boq_lines_boq_id_position",
                table: "boq_lines",
                columns: new[] { "boq_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_boqs_agreed_by_id",
                table: "boqs",
                column: "agreed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_boqs_author_id",
                table: "boqs",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_boqs_number",
                table: "boqs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boqs_project_id_kind_status",
                table: "boqs",
                columns: new[] { "project_id", "kind", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Permissions back: boq:create becomes statement:create again, with its grants. statement:send comes back for
            // super admins only, as seeded.
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "statement:create", "Create client statements of materials supplied", "execution" },
                    { "statement:send", "Send client statements", "execution" }
                });

            migrationBuilder.Sql(
                "INSERT INTO role_permissions (permission_code, role_code) " +
                "SELECT 'statement:create', role_code FROM role_permissions WHERE permission_code = 'boq:create';");
            migrationBuilder.Sql("INSERT INTO role_permissions (permission_code, role_code) VALUES ('statement:send', 'super_admin');");
            migrationBuilder.Sql("UPDATE user_permission_overrides SET permission_code = 'statement:create' WHERE permission_code = 'boq:create';");
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission_code = 'boq:create';");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "boq:create");

            migrationBuilder.DropTable(
                name: "boq_deliveries");

            migrationBuilder.DropTable(
                name: "boq_lines");

            migrationBuilder.DropTable(
                name: "boq_sequences");

            migrationBuilder.DropTable(
                name: "boqs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.DropColumn(
                name: "previous_supervision_rate",
                table: "project_payment_changes");

            migrationBuilder.DropColumn(
                name: "supervision_rate",
                table: "project_payment_changes");

            migrationBuilder.CreateTable(
                name: "client_statements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    client_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    agreed_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    includes_earlier = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    number = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    received_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    since = table.Column<DateOnly>(type: "date", nullable: true)
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
                name: "client_statement_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    statement_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true)
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
                    material_id = table.Column<long>(type: "bigint", nullable: false),
                    statement_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_earlier = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_listed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing')");

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
        }
    }
}
