using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_budgets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    set_by_id = table.Column<long>(type: "bigint", nullable: false),
                    set_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_budgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_budgets_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_budgets_users_set_by_id",
                        column: x => x.set_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_budget_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    team_id = table.Column<long>(type: "bigint", nullable: true),
                    category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_budget_lines", x => x.id);
                    table.CheckConstraint("ck_project_budget_lines_amount_non_negative", "`amount_usd` >= 0");
                    table.CheckConstraint("ck_project_budget_lines_one_key", "(`team_id` IS NULL) <> (`category` IS NULL)");
                    table.ForeignKey(
                        name: "fk_project_budget_lines_project_budgets_budget_id",
                        column: x => x.budget_id,
                        principalTable: "project_budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_budget_lines_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_budget_lines_budget_id_category",
                table: "project_budget_lines",
                columns: new[] { "budget_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_budget_lines_budget_id_team_id",
                table: "project_budget_lines",
                columns: new[] { "budget_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_budget_lines_team_id",
                table: "project_budget_lines",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_budgets_project_id_version",
                table: "project_budgets",
                columns: new[] { "project_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_budgets_set_by_id",
                table: "project_budgets",
                column: "set_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_budget_lines");

            migrationBuilder.DropTable(
                name: "project_budgets");
        }
    }
}
