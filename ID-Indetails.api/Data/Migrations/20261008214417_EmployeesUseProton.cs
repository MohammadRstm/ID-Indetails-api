using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployeesUseProton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "proton:use",
                column: "description",
                value: "Ask Proton, the company assistant, which proposes changes only for the person to confirm");

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "proton:use", "employee" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "proton:use", "employee" });

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "proton:use",
                column: "description",
                value: "Ask Proton, the read-only company assistant");
        }
    }
}
