using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OverheadGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overhead_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overhead_groups", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "overhead_groups",
                columns: new[] { "id", "created_at", "name", "sort_order" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Software & services", 10 },
                    { 2L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Operating costs", 20 },
                    { 3L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Rent & utilities", 30 },
                    { 4L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Internet & phone", 40 },
                    { 5L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Marketing & advertising", 50 },
                    { 6L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Professional fees", 60 },
                    { 7L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Insurance", 70 },
                    { 8L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Office supplies", 80 },
                    { 9L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Equipment & maintenance", 90 },
                    { 10L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Vehicles & transport", 100 },
                    { 11L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bank fees", 110 },
                    { 12L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Taxes & licences", 120 },
                    { 13L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Training & memberships", 130 },
                    { 14L, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Other", 10000 }
                });

            // Every existing line moves into the category of its old group: software (1) or operating (2).
            migrationBuilder.AddColumn<long>(
                name: "group_id",
                table: "overhead_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("UPDATE `overhead_items` SET `group_id` = CASE `cost_group` WHEN 'software' THEN 1 ELSE 2 END;");

            migrationBuilder.AlterColumn<long>(
                name: "group_id",
                table: "overhead_items",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "ck_overhead_items_cost_group",
                table: "overhead_items");

            migrationBuilder.DropColumn(
                name: "cost_group",
                table: "overhead_items");

            migrationBuilder.CreateIndex(
                name: "ix_overhead_items_group_id",
                table: "overhead_items",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_overhead_groups_name",
                table: "overhead_groups",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_overhead_items_overhead_groups_group_id",
                table: "overhead_items",
                column: "group_id",
                principalTable: "overhead_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to two groups: the first category is software, every other one operating.
            migrationBuilder.AddColumn<string>(
                name: "cost_group",
                table: "overhead_items",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "operating")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE `overhead_items` SET `cost_group` = CASE `group_id` WHEN 1 THEN 'software' ELSE 'operating' END;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_overhead_items_cost_group",
                table: "overhead_items",
                sql: "`cost_group` IN ('software', 'operating')");

            migrationBuilder.DropForeignKey(
                name: "fk_overhead_items_overhead_groups_group_id",
                table: "overhead_items");

            migrationBuilder.DropIndex(
                name: "ix_overhead_items_group_id",
                table: "overhead_items");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "overhead_items");

            migrationBuilder.DropTable(
                name: "overhead_groups");
        }
    }
}
