using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "box_state",
                table: "projects",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "box_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_box_entries", x => x.id);
                    table.CheckConstraint("ck_box_entries_amount_not_zero", "`amount` <> 0 OR `kind` = 'removed'");
                    table.CheckConstraint("ck_box_entries_kind", "`kind` IN ('payment_in', 'payment_voided', 'transfer', 'removed', 'readded')");
                    table.ForeignKey(
                        name: "fk_box_entries_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_box_entries_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_box_entries_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Backfill: today's open sites are in the box with their live payments; closed ones are out.
            migrationBuilder.Sql(
                "UPDATE `projects` SET `box_state` = IF(`status` IN ('active', 'hold'), 'in', 'removed') WHERE `domain_code` = 'execution';");
            migrationBuilder.Sql(
                "INSERT INTO `box_entries` (`kind`, `project_id`, `payment_id`, `amount`, `created_at`, `created_by_id`) " +
                "SELECT 'payment_in', p.`project_id`, p.`id`, p.`amount_usd`, p.`recorded_at`, p.`recorded_by_id` " +
                "FROM `payments` p JOIN `projects` pr ON pr.`id` = p.`project_id` " +
                "WHERE pr.`domain_code` = 'execution' AND pr.`box_state` = 'in' AND p.`voided_at` IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_box_state",
                table: "projects",
                sql: "`box_state` IS NULL OR `box_state` IN ('in', 'pending', 'removed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_box_state_execution_only",
                table: "projects",
                sql: "(`domain_code` = 'execution') = (`box_state` IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_box_entries_created_at",
                table: "box_entries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_box_entries_created_by_id",
                table: "box_entries",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_box_entries_payment_id",
                table: "box_entries",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_box_entries_project_id_kind",
                table: "box_entries",
                columns: new[] { "project_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "box_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_box_state",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_box_state_execution_only",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "box_state",
                table: "projects");
        }
    }
}
