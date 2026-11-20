using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_department_plans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    team_id = table.Column<long>(type: "bigint", nullable: false),
                    planned_minutes = table.Column<int>(type: "int", nullable: false),
                    set_by_id = table.Column<long>(type: "bigint", nullable: false),
                    set_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_department_plans", x => x.id);
                    table.CheckConstraint("ck_project_department_plans_planned_minutes_positive", "`planned_minutes` > 0");
                    table.ForeignKey(
                        name: "fk_project_department_plans_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_department_plans_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_department_plans_users_set_by_id",
                        column: x => x.set_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_department_plans_project_id_team_id",
                table: "project_department_plans",
                columns: new[] { "project_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_department_plans_set_by_id",
                table: "project_department_plans",
                column: "set_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_department_plans_team_id",
                table: "project_department_plans",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_department_plans");
        }
    }
}
