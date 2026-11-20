using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Message ticks and "Seen by" (20 November 2026). Each read marker gains how far the person's devices have received
    /// the chat (the second grey tick); existing rows start empty, and reading counts as receiving. Announcements keep
    /// when each person read each one, from now on: what was read before has no time, only the read marker.
    /// </summary>
    public partial class MessageReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_delivered_message_id",
                table: "conversation_reads",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_reads",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_reads", x => new { x.message_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_message_reads_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_message_reads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_last_delivered_message_id",
                table: "conversation_reads",
                column: "last_delivered_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_reads_user_id",
                table: "message_reads",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_reads_messages_last_delivered_message_id",
                table: "conversation_reads",
                column: "last_delivered_message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversation_reads_messages_last_delivered_message_id",
                table: "conversation_reads");

            migrationBuilder.DropTable(
                name: "message_reads");

            migrationBuilder.DropIndex(
                name: "ix_conversation_reads_last_delivered_message_id",
                table: "conversation_reads");

            migrationBuilder.DropColumn(
                name: "last_delivered_message_id",
                table: "conversation_reads");
        }
    }
}
