using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoqPermits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_boqs_amounts_by_kind",
                table: "boqs");

            migrationBuilder.AddColumn<decimal>(
                name: "permits_price",
                table: "projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // every site has permits from now on, none agreed yet: the BOQs agreed before had no permit lines
            migrationBuilder.Sql("UPDATE `projects` SET `permits_price` = 0 WHERE `domain_code` = 'execution';");

            migrationBuilder.AddColumn<decimal>(
                name: "permits",
                table: "project_payment_changes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "previous_permits",
                table: "project_payment_changes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "permits_usd",
                table: "boqs",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "nature",
                table: "boq_lines",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "work")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects",
                sql: "(`domain_code` = 'execution' AND `project_fee` IS NOT NULL AND `permits_price` IS NOT NULL) OR (`domain_code` <> 'execution' AND `project_fee` IS NULL AND `permits_price` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_permits_price_non_negative",
                table: "projects",
                sql: "`permits_price` IS NULL OR `permits_price` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_boqs_amounts_by_kind",
                table: "boqs",
                sql: "`kind` = 'vo' OR (`subtotal_usd` >= 0 AND `total_usd` >= 0 AND `permits_usd` >= 0 AND (`adjustment_type` <> 'total' OR `adjustment_value` >= 0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_boq_lines_nature",
                table: "boq_lines",
                sql: "`nature` IN ('work', 'permit')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_permits_price_non_negative",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_boqs_amounts_by_kind",
                table: "boqs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_boq_lines_nature",
                table: "boq_lines");

            migrationBuilder.DropColumn(
                name: "permits_price",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "permits",
                table: "project_payment_changes");

            migrationBuilder.DropColumn(
                name: "previous_permits",
                table: "project_payment_changes");

            migrationBuilder.DropColumn(
                name: "permits_usd",
                table: "boqs");

            migrationBuilder.DropColumn(
                name: "nature",
                table: "boq_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_execution_money_fields",
                table: "projects",
                sql: "(`domain_code` = 'execution' AND `project_fee` IS NOT NULL) OR (`domain_code` <> 'execution' AND `project_fee` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_boqs_amounts_by_kind",
                table: "boqs",
                sql: "`kind` = 'vo' OR (`subtotal_usd` >= 0 AND `total_usd` >= 0 AND (`adjustment_type` <> 'total' OR `adjustment_value` >= 0))");
        }
    }
}
