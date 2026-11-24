using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Custom chat groups (24 November 2026): a fourth kind of conversation, <c>custom_group</c>, with a name, an
    /// optional photo and the members a holder of the new <c>message:manage_groups</c> permission picked
    /// (<c>conversation_members</c>). Only custom groups have a name or a photo. The permission goes to super admins.
    /// Existing chats are untouched.
    /// </summary>
    public partial class ChatGroups : Migration
    {
        private const string ShapeBefore =
            "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id`)"
            + " OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL)"
            + " OR (`type` = 'announcements' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL)";

        private const string ShapeAfter =
            "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id` AND `name` IS NULL AND `photo_file_id` IS NULL)"
            + " OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NULL AND `photo_file_id` IS NULL)"
            + " OR (`type` = 'announcements' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NULL AND `photo_file_id` IS NULL)"
            + " OR (`type` = 'custom_group' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NOT NULL)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "conversations",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "photo_file_id",
                table: "conversations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversation_members",
                columns: table => new
                {
                    conversation_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_members", x => new { x.conversation_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_conversation_members_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "message:manage_groups", "Create chat groups of any staff, rename them, change who is in them and delete them", null });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "message:manage_groups", "super_admin" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_photo_file_id",
                table: "conversations",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_members_user_id",
                table: "conversation_members",
                column: "user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations",
                sql: ShapeAfter);

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations",
                sql: "`type` IN ('dm', 'group', 'announcements', 'custom_group')");

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_files_photo_file_id",
                table: "conversations",
                column: "photo_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the groups go with their messages (their mentions and attachments cascade), who read them and their
            // members; their photos and attachments' files are left to the upload cleanup, as attached to nothing
            migrationBuilder.Sql(
                "DELETE r FROM `conversation_reads` r JOIN `conversations` c ON c.`id` = r.`conversation_id` WHERE c.`type` = 'custom_group';"
                + " DELETE x FROM `message_reads` x JOIN `messages` m ON m.`id` = x.`message_id` JOIN `conversations` c ON c.`id` = m.`conversation_id` WHERE c.`type` = 'custom_group';"
                + " DELETE m FROM `messages` m JOIN `conversations` c ON c.`id` = m.`conversation_id` WHERE c.`type` = 'custom_group';"
                + " DELETE FROM `conversations` WHERE `type` = 'custom_group';");

            // grants and revokes made to people go before the permission itself
            migrationBuilder.Sql("DELETE FROM `user_permission_overrides` WHERE `permission_code` = 'message:manage_groups';");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_files_photo_file_id",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "conversation_members");

            migrationBuilder.DropIndex(
                name: "ix_conversations_photo_file_id",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "message:manage_groups", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "message:manage_groups");

            migrationBuilder.DropColumn(
                name: "name",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "photo_file_id",
                table: "conversations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations",
                sql: ShapeBefore);

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations",
                sql: "`type` IN ('dm', 'group', 'announcements')");
        }
    }
}
