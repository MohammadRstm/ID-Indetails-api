using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The markup goes: an Execution project no longer has one, and its check constraints are rewritten without it.
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_material_markup_range",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "material_markup",
                table: "projects");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects",
                sql: "(`domain_code` = 'execution' AND `project_fee` IS NOT NULL) OR (`domain_code` <> 'execution' AND `project_fee` IS NULL)");

            // project_materials becomes site_expenses. A table made new and filled from the old one keeps every constraint
            // and index named as the model names them; the rows keep their ids, which activity events point at.
            migrationBuilder.CreateTable(
                name: "site_expenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    supplier = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END", stored: true),
                    spent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "materials")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    account_label_id = table.Column<long>(type: "bigint", nullable: true),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    client_ref = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_expenses", x => x.id);
                    table.CheckConstraint("ck_site_expenses_amount_positive", "`amount` IS NULL OR `amount` > 0");
                    table.CheckConstraint("ck_site_expenses_category", "`category` IN ('materials', 'contractor', 'equipment', 'fees', 'other')");
                    table.CheckConstraint("ck_site_expenses_has_cost", "(COALESCE(`quantity`, 0) > 0 AND COALESCE(`unit_cost`, 0) > 0) OR COALESCE(`amount`, 0) > 0");
                    table.CheckConstraint("ck_site_expenses_quantity_positive", "`quantity` IS NULL OR `quantity` > 0");
                    table.CheckConstraint("ck_site_expenses_unit_cost_non_negative", "`unit_cost` IS NULL OR `unit_cost` >= 0");
                    table.ForeignKey(
                        name: "fk_site_expenses_account_labels_account_label_id",
                        column: x => x.account_label_id,
                        principalTable: "account_labels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_expenses_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_expenses_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_site_expenses_account_label_id",
                table: "site_expenses",
                column: "account_label_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_expenses_added_by_id_client_ref",
                table: "site_expenses",
                columns: new[] { "added_by_id", "client_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_expenses_project_id",
                table: "site_expenses",
                column: "project_id");

            // Materials keep their rows (client_price is dropped with the markup). Each is dated by the day it was added in
            // the company time zone; if the server has no time zone tables, by that day in UTC.
            migrationBuilder.Sql(
                "INSERT INTO site_expenses (id, project_id, name, quantity, unit, unit_cost, amount, currency, supplier, note, spent_on, category, added_at, added_by_id, updated_at, deleted_at, client_ref) " +
                "SELECT m.id, m.project_id, m.name, m.quantity, m.unit, m.unit_cost, m.amount, m.currency, m.supplier, m.note, " +
                "DATE(COALESCE(CONVERT_TZ(m.added_at, '+00:00', (SELECT time_zone FROM company_settings WHERE id = 1)), m.added_at)), " +
                "'materials', m.added_at, m.added_by_id, m.updated_at, m.deleted_at, m.client_ref " +
                "FROM project_materials m ORDER BY m.id;");

            // An Execution site's project expenses move over (a project expense is never zero). Its categories are
            // folded into the site's: assets and hardware are equipment, services and software are fees, travel and
            // anything else is other. A row with no author is the project owner's.
            migrationBuilder.Sql(
                "INSERT INTO site_expenses (project_id, name, amount, currency, note, spent_on, category, added_at, added_by_id, deleted_at) " +
                "SELECT e.project_id, e.label, e.amount, e.currency, e.note, e.spent_on, " +
                "CASE e.category WHEN 'contractor' THEN 'contractor' WHEN 'assets' THEN 'equipment' WHEN 'hardware' THEN 'equipment' " +
                "WHEN 'services' THEN 'fees' WHEN 'software' THEN 'fees' ELSE 'other' END, " +
                "e.created_at, COALESCE(e.created_by_id, p.owner_id), e.deleted_at " +
                "FROM project_expenses e JOIN projects p ON p.id = e.project_id " +
                "WHERE p.domain_code = 'execution' AND e.amount > 0 ORDER BY e.id;");
            migrationBuilder.Sql(
                "DELETE e FROM project_expenses e JOIN projects p ON p.id = e.project_id WHERE p.domain_code = 'execution' AND e.amount > 0;");

            migrationBuilder.DropTable(
                name: "project_materials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_materials",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    client_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    client_ref = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    supplier = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END", stored: true),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_materials", x => x.id);
                    table.CheckConstraint("ck_project_materials_amount_positive", "`amount` IS NULL OR `amount` > 0");
                    table.CheckConstraint("ck_project_materials_client_price_non_negative", "`client_price` IS NULL OR `client_price` >= 0");
                    table.CheckConstraint("ck_project_materials_has_cost", "(COALESCE(`quantity`, 0) > 0 AND COALESCE(`unit_cost`, 0) > 0) OR COALESCE(`amount`, 0) > 0");
                    table.CheckConstraint("ck_project_materials_quantity_positive", "`quantity` IS NULL OR `quantity` > 0");
                    table.CheckConstraint("ck_project_materials_unit_cost_non_negative", "`unit_cost` IS NULL OR `unit_cost` >= 0");
                    table.ForeignKey(
                        name: "fk_project_materials_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_materials_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_materials_added_by_id_client_ref",
                table: "project_materials",
                columns: new[] { "added_by_id", "client_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_materials_project_id",
                table: "project_materials",
                column: "project_id");

            // Materials go back as materials, with no client price (they follow the project's markup again). Every other
            // site expense goes back to being a project expense of its site, in the old categories.
            migrationBuilder.Sql(
                "INSERT INTO project_materials (id, project_id, name, quantity, unit, unit_cost, amount, currency, supplier, note, added_at, added_by_id, updated_at, deleted_at, client_ref) " +
                "SELECT s.id, s.project_id, s.name, s.quantity, s.unit, s.unit_cost, s.amount, s.currency, s.supplier, s.note, s.added_at, s.added_by_id, s.updated_at, s.deleted_at, s.client_ref " +
                "FROM site_expenses s WHERE s.category = 'materials' ORDER BY s.id;");
            migrationBuilder.Sql(
                "INSERT INTO project_expenses (project_id, label, amount, currency, category, spent_on, note, created_at, created_by_id, deleted_at) " +
                "SELECT s.project_id, s.name, s.total, s.currency, " +
                "CASE s.category WHEN 'contractor' THEN 'contractor' WHEN 'equipment' THEN 'hardware' WHEN 'fees' THEN 'services' ELSE 'other' END, " +
                "s.spent_on, s.note, s.added_at, s.added_by_id, s.deleted_at " +
                "FROM site_expenses s WHERE s.category <> 'materials' ORDER BY s.id;");

            migrationBuilder.DropTable(
                name: "site_expenses");

            // The markup comes back at none, which every Execution project needs.
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects");

            migrationBuilder.AddColumn<decimal>(
                name: "material_markup",
                table: "projects",
                type: "decimal(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.Sql("UPDATE projects SET material_markup = 0 WHERE domain_code = 'execution';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects",
                sql: "(`domain_code` = 'execution' AND `project_fee` IS NOT NULL AND `material_markup` IS NOT NULL) OR (`domain_code` <> 'execution' AND `project_fee` IS NULL AND `material_markup` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_material_markup_range",
                table: "projects",
                sql: "`material_markup` IS NULL OR `material_markup` BETWEEN 0 AND 5");
        }
    }
}
