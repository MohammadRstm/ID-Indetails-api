using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_notification_mutes",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    muted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notification_mutes", x => new { x.user_id, x.category });
                    table.CheckConstraint("ck_user_notification_mutes_category", "`category` IN ('work', 'requirements', 'projects', 'meetings', 'sites', 'money', 'people', 'messages')");
                    table.ForeignKey(
                        name: "fk_user_notification_mutes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_notification_settings",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    quiet_from_min = table.Column<short>(type: "smallint", nullable: true),
                    quiet_to_min = table.Column<short>(type: "smallint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notification_settings", x => x.user_id);
                    table.CheckConstraint("ck_user_notification_settings_quiet", "(`quiet_from_min` IS NULL AND `quiet_to_min` IS NULL) OR (`quiet_from_min` BETWEEN 0 AND 1439 AND `quiet_to_min` BETWEEN 0 AND 1439)");
                    table.ForeignKey(
                        name: "fk_user_notification_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_notification_mutes");

            migrationBuilder.DropTable(
                name: "user_notification_settings");
        }
    }
}
