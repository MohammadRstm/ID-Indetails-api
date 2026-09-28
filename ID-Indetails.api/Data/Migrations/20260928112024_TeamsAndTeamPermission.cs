using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TeamsAndTeamPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "team:manage", "Create, rename and delete teams", null });

            migrationBuilder.InsertData(
                table: "teams",
                columns: new[] { "id", "created_at", "domain_code", "name" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Engineering" },
                    { 2L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Design" },
                    { 3L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Data" },
                    { 4L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Marketing" },
                    { 5L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Operations" },
                    { 6L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "design", "Platform" },
                    { 7L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "execution", "Site Operations" },
                    { 8L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "execution", "Procurement" },
                    { 9L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "execution", "Quantity Surveying" },
                    { 10L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "execution", "Safety" },
                    { 11L, new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), "execution", "Project Management" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "team:manage", "admin" },
                    { "team:manage", "super_admin" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "team:manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "team:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 7L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 8L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 9L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 10L);

            migrationBuilder.DeleteData(
                table: "teams",
                keyColumn: "id",
                keyValue: 11L);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "team:manage");
        }
    }
}
