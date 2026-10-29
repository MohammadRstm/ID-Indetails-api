using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfitFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blocked_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payload = table.Column<string>(type: "text", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requested_by_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decided_by_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    result_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocked_changes", x => x.id);
                    table.CheckConstraint("ck_blocked_changes_decided", "(`status` = 'pending') = (`decided_at` IS NULL)");
                    table.CheckConstraint("ck_blocked_changes_kind", "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing')");
                    table.CheckConstraint("ck_blocked_changes_status", "`status` IN ('pending', 'forced', 'dismissed')");
                    table.ForeignKey(
                        name: "fk_blocked_changes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_blocked_changes_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_blocked_changes_users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_blocked_changes_decided_by_id",
                table: "blocked_changes",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_blocked_changes_project_id_status",
                table: "blocked_changes",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_blocked_changes_requested_by_id",
                table: "blocked_changes",
                column: "requested_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocked_changes");
        }
    }
}
