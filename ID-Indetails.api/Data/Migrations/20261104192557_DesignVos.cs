using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DesignVos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.CreateTable(
                name: "design_vos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    split = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total_usd = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    tva_rate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    outside_scope_minutes = table.Column<int>(type: "int", nullable: true),
                    outside_scope_from = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    applied_by_id = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    cancelled_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_vos", x => x.id);
                    table.CheckConstraint("ck_design_vos_split", "`split` IN ('shares', 'departments')");
                    table.CheckConstraint("ck_design_vos_status", "`status` IN ('pending', 'applied', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_design_vos_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_design_vos_users_applied_by_id",
                        column: x => x.applied_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_design_vos_users_cancelled_by_id",
                        column: x => x.cancelled_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_design_vos_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_design_vos_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "design_vo_allocations",
                columns: table => new
                {
                    design_vo_id = table.Column<long>(type: "bigint", nullable: false),
                    team_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_vo_allocations", x => new { x.design_vo_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_design_vo_allocations_design_vos_design_vo_id",
                        column: x => x.design_vo_id,
                        principalTable: "design_vos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_design_vo_allocations_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "design_vo_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    design_vo_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_vo_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_design_vo_lines_design_vos_design_vo_id",
                        column: x => x.design_vo_id,
                        principalTable: "design_vos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department', 'design_vo')");

            migrationBuilder.CreateIndex(
                name: "ix_design_vo_allocations_team_id",
                table: "design_vo_allocations",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_vo_lines_design_vo_id_position",
                table: "design_vo_lines",
                columns: new[] { "design_vo_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_applied_by_id",
                table: "design_vos",
                column: "applied_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_cancelled_by_id",
                table: "design_vos",
                column: "cancelled_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_created_by_id",
                table: "design_vos",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_number",
                table: "design_vos",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_project_id_status",
                table: "design_vos",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_design_vos_sent_by_id",
                table: "design_vos",
                column: "sent_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "design_vo_allocations");

            migrationBuilder.DropTable(
                name: "design_vo_lines");

            migrationBuilder.DropTable(
                name: "design_vos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department')");
        }
    }
}
