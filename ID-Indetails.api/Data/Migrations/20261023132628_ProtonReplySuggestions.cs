using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtonReplySuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage");

            migrationBuilder.AddColumn<bool>(
                name: "proton_reply_suggestions",
                table: "users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "proton_reply_suggestions",
                table: "company_settings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage",
                sql: "`channel` IN ('text', 'voice', 'suggestion')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage");

            migrationBuilder.DropColumn(
                name: "proton_reply_suggestions",
                table: "users");

            migrationBuilder.DropColumn(
                name: "proton_reply_suggestions",
                table: "company_settings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage",
                sql: "`channel` IN ('text', 'voice')");
        }
    }
}
