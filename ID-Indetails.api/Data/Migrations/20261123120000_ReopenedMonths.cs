using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Closed months a super admin opens again for logging, until a day (23 November 2026). Empty until one is reopened.
    /// </summary>
    public partial class ReopenedMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reopened_months",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    open_until = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reopened_by_id = table.Column<long>(type: "bigint", nullable: false),
                    reopened_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reopened_months", x => x.month);
                    table.ForeignKey(
                        name: "fk_reopened_months_users_reopened_by_id",
                        column: x => x.reopened_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_reopened_months_reopened_by_id",
                table: "reopened_months",
                column: "reopened_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reopened_months");
        }
    }
}
