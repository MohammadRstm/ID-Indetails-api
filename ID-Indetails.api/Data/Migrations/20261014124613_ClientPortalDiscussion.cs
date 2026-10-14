using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalDiscussion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "portal_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<long>(type: "bigint", nullable: false),
                    item_title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    author_user_id = table.Column<long>(type: "bigint", nullable: true),
                    author_account_id = table.Column<long>(type: "bigint", nullable: true),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    client_token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    edited_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_comments", x => x.id);
                    table.CheckConstraint("ck_portal_comments_body_unless_deleted", "`body` IS NOT NULL OR `deleted_at` IS NOT NULL");
                    table.CheckConstraint("ck_portal_comments_one_author", "(`author_user_id` IS NULL) <> (`author_account_id` IS NULL)");
                    table.ForeignKey(
                        name: "fk_portal_comments_portal_accounts_author_account_id",
                        column: x => x.author_account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_comments_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: true),
                    author_account_id = table.Column<long>(type: "bigint", nullable: true),
                    body = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    client_token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    edited_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_messages", x => x.id);
                    table.CheckConstraint("ck_portal_messages_one_author", "(`author_user_id` IS NULL) <> (`author_account_id` IS NULL)");
                    table.ForeignKey(
                        name: "fk_portal_messages_portal_accounts_author_account_id",
                        column: x => x.author_account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_messages_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_messages_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_id = table.Column<long>(type: "bigint", nullable: true),
                    payload = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    digest_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_notifications", x => x.id);
                    table.CheckConstraint("ck_portal_notifications_count_positive", "`count` > 0");
                    table.CheckConstraint("ck_portal_notifications_kind", "`kind` IN ('team_message', 'team_comment', 'mention')");
                    table.ForeignKey(
                        name: "fk_portal_notifications_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_notifications_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_reads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    account_id = table.Column<long>(type: "bigint", nullable: true),
                    last_message_id = table.Column<long>(type: "bigint", nullable: true),
                    last_comment_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_reads", x => x.id);
                    table.CheckConstraint("ck_portal_reads_one_reader", "(`user_id` IS NULL) <> (`account_id` IS NULL)");
                    table.ForeignKey(
                        name: "fk_portal_reads_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_reads_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_reads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_mentions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    comment_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    account_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_mentions", x => x.id);
                    table.CheckConstraint("ck_portal_mentions_one_person", "(`user_id` IS NULL) <> (`account_id` IS NULL)");
                    table.CheckConstraint("ck_portal_mentions_one_place", "(`message_id` IS NULL) <> (`comment_id` IS NULL)");
                    table.ForeignKey(
                        name: "fk_portal_mentions_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_mentions_portal_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "portal_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_mentions_portal_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "portal_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_mentions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_portal_comments_author_account_id_client_token",
                table: "portal_comments",
                columns: new[] { "author_account_id", "client_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_comments_author_user_id_client_token",
                table: "portal_comments",
                columns: new[] { "author_user_id", "client_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_comments_project_id_id",
                table: "portal_comments",
                columns: new[] { "project_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_comments_project_id_item_id_id",
                table: "portal_comments",
                columns: new[] { "project_id", "item_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_account_id",
                table: "portal_mentions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_comment_id_account_id",
                table: "portal_mentions",
                columns: new[] { "comment_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_comment_id_user_id",
                table: "portal_mentions",
                columns: new[] { "comment_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_message_id_account_id",
                table: "portal_mentions",
                columns: new[] { "message_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_message_id_user_id",
                table: "portal_mentions",
                columns: new[] { "message_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_mentions_user_id",
                table: "portal_mentions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_messages_author_account_id_client_token",
                table: "portal_messages",
                columns: new[] { "author_account_id", "client_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_messages_author_user_id_client_token",
                table: "portal_messages",
                columns: new[] { "author_user_id", "client_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_messages_project_id_id",
                table: "portal_messages",
                columns: new[] { "project_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_notifications_account_id_digest_key",
                table: "portal_notifications",
                columns: new[] { "account_id", "digest_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_notifications_account_id_read_at_id",
                table: "portal_notifications",
                columns: new[] { "account_id", "read_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_notifications_project_id",
                table: "portal_notifications",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_reads_account_id",
                table: "portal_reads",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_reads_project_id_account_id",
                table: "portal_reads",
                columns: new[] { "project_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_reads_project_id_user_id",
                table: "portal_reads",
                columns: new[] { "project_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_reads_user_id",
                table: "portal_reads",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portal_mentions");

            migrationBuilder.DropTable(
                name: "portal_notifications");

            migrationBuilder.DropTable(
                name: "portal_reads");

            migrationBuilder.DropTable(
                name: "portal_comments");

            migrationBuilder.DropTable(
                name: "portal_messages");
        }
    }
}
