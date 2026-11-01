using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_sent_keys",
                columns: table => new
                {
                    recipient_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_sent_keys", x => new { x.recipient_id, x.key });
                    table.ForeignKey(
                        name: "fk_notification_sent_keys_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // What was sent once until now is remembered by its notification, which is about to be deleted after a week.
            migrationBuilder.Sql("""
                INSERT INTO `notification_sent_keys` (`recipient_id`, `key`, `sent_at`)
                SELECT `recipient_id`, `digest_key`, `created_at` FROM `notifications` WHERE `digest_key` IS NOT NULL
                """);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_read_at_created_at",
                table: "notifications",
                columns: new[] { "recipient_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_occurred_at",
                table: "activity_events",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_sent_keys");

            migrationBuilder.DropIndex(
                name: "ix_notifications_created_at",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient_id_read_at_created_at",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_activity_events_occurred_at",
                table: "activity_events");
        }
    }
}
