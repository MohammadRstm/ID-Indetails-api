using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoqLineProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boq_line_progress",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    boq_line_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    recorded_on = table.Column<DateOnly>(type: "date", nullable: false),
                    executed_quantity = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    recorded_by_id = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boq_line_progress", x => x.id);
                    table.CheckConstraint("ck_boq_line_progress_executed_quantity_non_negative", "`executed_quantity` >= 0");
                    table.ForeignKey(
                        name: "fk_boq_line_progress_boq_lines_boq_line_id",
                        column: x => x.boq_line_id,
                        principalTable: "boq_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_boq_line_progress_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_boq_line_progress_users_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_boq_line_progress_boq_line_id_recorded_on",
                table: "boq_line_progress",
                columns: new[] { "boq_line_id", "recorded_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boq_line_progress_project_id_recorded_on",
                table: "boq_line_progress",
                columns: new[] { "project_id", "recorded_on" });

            migrationBuilder.CreateIndex(
                name: "ix_boq_line_progress_recorded_by_id",
                table: "boq_line_progress",
                column: "recorded_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boq_line_progress");
        }
    }
}
