using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteLogVoiceNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "voice_file_id",
                table: "site_logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_logs_voice_file_id",
                table: "site_logs",
                column: "voice_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_site_logs_files_voice_file_id",
                table: "site_logs",
                column: "voice_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_site_logs_files_voice_file_id",
                table: "site_logs");

            migrationBuilder.DropIndex(
                name: "ix_site_logs_voice_file_id",
                table: "site_logs");

            migrationBuilder.DropColumn(
                name: "voice_file_id",
                table: "site_logs");
        }
    }
}
