using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeetingsInBothDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "meeting:create",
                column: "domain_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "meeting:manage",
                column: "domain_code",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "meeting:create",
                column: "domain_code",
                value: "design");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "meeting:manage",
                column: "domain_code",
                value: "design");
        }
    }
}
