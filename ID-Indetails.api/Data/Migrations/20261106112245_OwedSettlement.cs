using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OwedSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "settled_owed_as",
                table: "projects",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "settled_owed_usd",
                table: "projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "employee:manage",
                column: "description",
                value: "Edit employee profiles, departments and status");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "team:manage",
                column: "description",
                value: "Create, rename and delete departments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_settled_owed",
                table: "projects",
                sql: "(`settled_owed_usd` IS NULL AND `settled_owed_as` IS NULL) OR (`settled_owed_usd` > 0 AND `settled_owed_as` IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_settled_owed_as",
                table: "projects",
                sql: "`settled_owed_as` IN ('excused', 'written_off')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_settled_owed",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_settled_owed_as",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "settled_owed_as",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "settled_owed_usd",
                table: "projects");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "employee:manage",
                column: "description",
                value: "Edit employee profiles, teams and status");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "team:manage",
                column: "description",
                value: "Create, rename and delete teams");
        }
    }
}
