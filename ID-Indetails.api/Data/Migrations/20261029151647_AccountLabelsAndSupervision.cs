using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountLabelsAndSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "supervision_rate",
                table: "projects",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.Sql("UPDATE projects SET supervision_rate = 0.10 WHERE domain_code = 'execution' AND billing_type = 'client';");

            migrationBuilder.CreateTable(
                name: "account_labels",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false),
                    archived_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_labels", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "account_labels",
                columns: new[] { "id", "archived_at", "created_at", "name", "position" },
                values: new object[,]
                {
                    { 1L, null, new DateTime(2026, 10, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Concrete", 1 },
                    { 2L, null, new DateTime(2026, 10, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Finishing", 2 },
                    { 3L, null, new DateTime(2026, 10, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Permits", 3 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_supervision_rate_range",
                table: "projects",
                sql: "`supervision_rate` IS NULL OR `supervision_rate` BETWEEN 0 AND 1");

            migrationBuilder.CreateIndex(
                name: "ix_account_labels_name",
                table: "account_labels",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_labels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_supervision_rate_range",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "supervision_rate",
                table: "projects");
        }
    }
}
