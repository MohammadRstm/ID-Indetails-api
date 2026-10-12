using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtonVoiceUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "proton_usage",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // rows before voice were all typed; an empty value would fail the check constraint below
                defaultValue: "text")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "voice_seconds",
                table: "proton_usage",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage",
                sql: "`channel` IN ('text', 'voice')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proton_usage_voice_seconds_non_negative",
                table: "proton_usage",
                sql: "`voice_seconds` IS NULL OR `voice_seconds` >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proton_usage_channel",
                table: "proton_usage");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proton_usage_voice_seconds_non_negative",
                table: "proton_usage");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "proton_usage");

            migrationBuilder.DropColumn(
                name: "voice_seconds",
                table: "proton_usage");
        }
    }
}
