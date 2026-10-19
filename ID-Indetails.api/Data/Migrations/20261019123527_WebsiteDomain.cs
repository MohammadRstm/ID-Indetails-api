using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "domains",
                columns: new[] { "code", "name" },
                values: new object[] { "website", "Website" });

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:manage",
                column: "domain_code",
                value: "website");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:view",
                column: "domain_code",
                value: "website");

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "website:manage", "admin" },
                    { "website:view", "admin" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the permissions leave the domain before it goes; someone still in it keeps the row (and this fails) on purpose
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:manage",
                column: "domain_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:view",
                column: "domain_code",
                value: null);

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "website:manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "website:view", "admin" });

            migrationBuilder.DeleteData(
                table: "domains",
                keyColumn: "code",
                keyValue: "website");
        }
    }
}
