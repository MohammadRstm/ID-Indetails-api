using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebPushDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_devices_platform",
                table: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_devices_provider",
                table: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "web_push_auth",
                table: "user_devices",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "web_push_endpoint",
                table: "user_devices",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "web_push_p256dh",
                table: "user_devices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_devices_platform",
                table: "user_devices",
                sql: "`platform` IN ('ios', 'android', 'web')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_devices_provider",
                table: "user_devices",
                sql: "`provider` IN ('expo', 'web_push')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_devices_web_push",
                table: "user_devices",
                sql: "(`provider` = 'web_push') = (`web_push_endpoint` IS NOT NULL AND `web_push_p256dh` IS NOT NULL AND `web_push_auth` IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens",
                sql: "`client` IN ('web', 'mobile', 'installed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_devices_platform",
                table: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_devices_provider",
                table: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_devices_web_push",
                table: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens");

            // what the older constraints don't allow: browsers stop getting pushes, installed-app sessions become website ones
            migrationBuilder.Sql("DELETE FROM `user_devices` WHERE `provider` = 'web_push';");
            migrationBuilder.Sql("UPDATE `refresh_tokens` SET `client` = 'web' WHERE `client` = 'installed';");

            migrationBuilder.DropColumn(
                name: "web_push_auth",
                table: "user_devices");

            migrationBuilder.DropColumn(
                name: "web_push_endpoint",
                table: "user_devices");

            migrationBuilder.DropColumn(
                name: "web_push_p256dh",
                table: "user_devices");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_devices_platform",
                table: "user_devices",
                sql: "`platform` IN ('ios', 'android')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_devices_provider",
                table: "user_devices",
                sql: "`provider` IN ('expo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_client",
                table: "refresh_tokens",
                sql: "`client` IN ('web', 'mobile')");
        }
    }
}
