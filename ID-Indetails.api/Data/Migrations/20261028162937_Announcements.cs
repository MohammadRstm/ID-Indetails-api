using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Announcements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "message:announce", "Write in Announcements, the chat everyone reads", null });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "message:announce", "super_admin" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations",
                sql: "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id`) OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL) OR (`type` = 'announcements' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations",
                sql: "`type` IN ('dm', 'group', 'announcements')");

            // the one Announcements chat, which everyone reads
            migrationBuilder.Sql("INSERT INTO `conversations` (`type`, `created_at`) VALUES ('announcements', UTC_TIMESTAMP(6));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the chat goes with its messages (their mentions and attachments cascade) and who read them
            migrationBuilder.Sql(
                "DELETE r FROM `conversation_reads` r JOIN `conversations` c ON c.`id` = r.`conversation_id` WHERE c.`type` = 'announcements';"
                + " DELETE m FROM `messages` m JOIN `conversations` c ON c.`id` = m.`conversation_id` WHERE c.`type` = 'announcements';"
                + " DELETE FROM `conversations` WHERE `type` = 'announcements';");

            // grants and revokes made to people go before the permission itself
            migrationBuilder.Sql("DELETE FROM `user_permission_overrides` WHERE `permission_code` = 'message:announce';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "message:announce", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "message:announce");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_shape",
                table: "conversations",
                sql: "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id`) OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_type",
                table: "conversations",
                sql: "`type` IN ('dm', 'group')");
        }
    }
}
