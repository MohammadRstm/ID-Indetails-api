using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MobileSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing token is the website's, which the check below needs; the API always writes the client.
            // Existing rows keep no session and no revoke reason.
            migrationBuilder.AddColumn<string>(
                name: "client",
                table: "refresh_tokens",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "web")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "revoke_reason",
                table: "refresh_tokens",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                table: "refresh_tokens",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                table: "refresh_tokens",
                column: "session_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens",
                sql: "`client` IN ('web', 'mobile')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_revoke_reason",
                table: "refresh_tokens",
                sql: "`revoke_reason` IN ('rotated', 'superseded', 'signed_out', 'revoked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_session_id",
                table: "refresh_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_revoke_reason",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "client",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revoke_reason",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "refresh_tokens");
        }
    }
}
