using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PortalUx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_opened_at",
                table: "portal_project_access",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "portal_messages",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "portal_messages",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "portal_hero_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_published = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_hero_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_portal_hero_images_portal_files_file_id",
                        column: x => x.file_id,
                        principalTable: "portal_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_hero_images_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portal_messages_body_unless_deleted",
                table: "portal_messages",
                sql: "`body` IS NOT NULL OR `deleted_at` IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_portal_hero_images_file_id",
                table: "portal_hero_images",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_hero_images_project_id_is_published_file_id",
                table: "portal_hero_images",
                columns: new[] { "project_id", "is_published", "file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_hero_images_project_id_is_published_sort_order",
                table: "portal_hero_images",
                columns: new[] { "project_id", "is_published", "sort_order" });

            // the hero each project had (draft and published) becomes its first hero image
            migrationBuilder.Sql(@"INSERT INTO `portal_hero_images` (`project_id`, `file_id`, `sort_order`, `is_published`)
SELECT `project_id`, `hero_file_id`, 0, 0 FROM `portal_settings` WHERE `hero_file_id` IS NOT NULL;");
            migrationBuilder.Sql(@"INSERT INTO `portal_hero_images` (`project_id`, `file_id`, `sort_order`, `is_published`)
SELECT `project_id`, `published_hero_file_id`, 0, 1 FROM `portal_settings` WHERE `published_hero_file_id` IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_portal_settings_portal_files_hero_file_id",
                table: "portal_settings");
            migrationBuilder.DropForeignKey(
                name: "fk_portal_settings_portal_files_published_hero_file_id",
                table: "portal_settings");
            migrationBuilder.DropIndex(
                name: "ix_portal_settings_hero_file_id",
                table: "portal_settings");
            migrationBuilder.DropIndex(
                name: "ix_portal_settings_published_hero_file_id",
                table: "portal_settings");
            migrationBuilder.DropColumn(
                name: "hero_file_id",
                table: "portal_settings");
            migrationBuilder.DropColumn(
                name: "published_hero_file_id",
                table: "portal_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_portal_messages_body_unless_deleted",
                table: "portal_messages");

            migrationBuilder.DropColumn(
                name: "last_opened_at",
                table: "portal_project_access");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "portal_messages");

            migrationBuilder.AddColumn<long>(
                name: "hero_file_id",
                table: "portal_settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "published_hero_file_id",
                table: "portal_settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "portal_messages",
                keyColumn: "body",
                keyValue: null,
                column: "body",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "portal_messages",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_portal_settings_hero_file_id",
                table: "portal_settings",
                column: "hero_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_settings_published_hero_file_id",
                table: "portal_settings",
                column: "published_hero_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_portal_settings_portal_files_hero_file_id",
                table: "portal_settings",
                column: "hero_file_id",
                principalTable: "portal_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_portal_settings_portal_files_published_hero_file_id",
                table: "portal_settings",
                column: "published_hero_file_id",
                principalTable: "portal_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // each list's first hero image goes back into the single hero columns
            migrationBuilder.Sql(@"UPDATE `portal_settings` s SET
  `hero_file_id` = (SELECT h.`file_id` FROM `portal_hero_images` h WHERE h.`project_id` = s.`project_id` AND h.`is_published` = 0 ORDER BY h.`sort_order`, h.`id` LIMIT 1),
  `published_hero_file_id` = (SELECT h.`file_id` FROM `portal_hero_images` h WHERE h.`project_id` = s.`project_id` AND h.`is_published` = 1 ORDER BY h.`sort_order`, h.`id` LIMIT 1);");

            migrationBuilder.DropTable(
                name: "portal_hero_images");
        }
    }
}
