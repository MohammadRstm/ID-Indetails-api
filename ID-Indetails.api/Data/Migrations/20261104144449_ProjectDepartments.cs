using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddColumn<long>(
                name: "team_id",
                table: "work_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_department_share_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    before = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    after = table.Column<string>(type: "text", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    changed_by_id = table.Column<long>(type: "bigint", nullable: false),
                    changed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_department_share_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_department_share_changes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_department_share_changes_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_departments",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    team_id = table.Column<long>(type: "bigint", nullable: false),
                    share = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_departments", x => new { x.project_id, x.team_id });
                    table.CheckConstraint("ck_project_departments_share", "`share` >= 0 AND `share` <= 1");
                    table.ForeignKey(
                        name: "fk_project_departments_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_departments_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_departments_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_work_entries_team_id",
                table: "work_entries",
                column: "team_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department')");

            migrationBuilder.CreateIndex(
                name: "ix_project_department_share_changes_changed_by_id",
                table: "project_department_share_changes",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_department_share_changes_project_id_changed_at",
                table: "project_department_share_changes",
                columns: new[] { "project_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_departments_added_by_id",
                table: "project_departments",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_departments_team_id",
                table: "project_departments",
                column: "team_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_entries_teams_team_id",
                table: "work_entries",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // the hours already logged count in their logger's department as it is now
            migrationBuilder.Sql("UPDATE `work_entries` w JOIN `users` u ON u.`id` = w.`user_id` SET w.`team_id` = u.`team_id` WHERE w.`team_id` IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_entries_teams_team_id",
                table: "work_entries");

            migrationBuilder.DropTable(
                name: "project_department_share_changes");

            migrationBuilder.DropTable(
                name: "project_departments");

            migrationBuilder.DropIndex(
                name: "ix_work_entries_team_id",
                table: "work_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "work_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw')");
        }
    }
}
