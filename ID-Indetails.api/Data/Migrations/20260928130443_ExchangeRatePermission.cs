using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRatePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "exchange_rate:manage", "Set the company-wide exchange rates used for payments", null });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "exchange_rate:manage", "super_admin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "exchange_rate:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "exchange_rate:manage");
        }
    }
}
