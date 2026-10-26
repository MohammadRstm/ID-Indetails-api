using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectPaymentChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_payment_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    material_markup = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    previous_material_markup = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    changed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    changed_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_payment_changes", x => x.id);
                    table.CheckConstraint("ck_project_payment_changes_amount_non_negative", "`amount` >= 0 AND (`previous_amount` IS NULL OR `previous_amount` >= 0)");
                    table.ForeignKey(
                        name: "fk_project_payment_changes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_payment_changes_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_payment_changes_changed_by_id",
                table: "project_payment_changes",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_payment_changes_project_id_changed_at",
                table: "project_payment_changes",
                columns: new[] { "project_id", "changed_at" });

            // Every client project starts its history with the payment it has today, as agreed at the start: Design's
            // customer payment, or Execution's project fee and markup.
            migrationBuilder.Sql(
                "INSERT INTO `project_payment_changes` " +
                "(`project_id`, `amount`, `previous_amount`, `material_markup`, `previous_material_markup`, `description`, `changed_at`, `changed_by_id`) " +
                "SELECT `id`, CASE WHEN `domain_code` = 'execution' THEN COALESCE(`project_fee`, 0) ELSE `revenue` END, NULL, " +
                "`material_markup`, NULL, 'Agreed at the start', `created_at`, `created_by_id` " +
                "FROM `projects` WHERE `billing_type` = 'client';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_payment_changes");
        }
    }
}
