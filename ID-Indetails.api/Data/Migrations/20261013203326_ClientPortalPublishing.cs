using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "section",
                table: "portal_uploads",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "is_hero",
                table: "portal_uploads",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "published_file_id",
                table: "portal_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_section",
                table: "portal_items",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "published_sort_order",
                table: "portal_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_title",
                table: "portal_items",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "removed_at",
                table: "portal_items",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "portal_settings",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    hero_file_id = table.Column<long>(type: "bigint", nullable: true),
                    published_hero_file_id = table.Column<long>(type: "bigint", nullable: true),
                    summary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    published_summary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<int>(type: "int", nullable: false),
                    published_revision = table.Column<int>(type: "int", nullable: true),
                    published_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    published_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_settings", x => x.project_id);
                    table.CheckConstraint("ck_portal_settings_revision_non_negative", "`revision` >= 0");
                    table.ForeignKey(
                        name: "fk_portal_settings_portal_files_hero_file_id",
                        column: x => x.hero_file_id,
                        principalTable: "portal_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_settings_portal_files_published_hero_file_id",
                        column: x => x.published_hero_file_id,
                        principalTable: "portal_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_settings_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_settings_users_published_by_id",
                        column: x => x.published_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portal_uploads_hero_or_section",
                table: "portal_uploads",
                sql: "(`is_hero` = 1 AND `section` IS NULL AND `replace_item_id` IS NULL) OR (`is_hero` = 0 AND `section` IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_portal_items_project_id_published_section_published_sort_ord",
                table: "portal_items",
                columns: new[] { "project_id", "published_section", "published_sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_items_published_file_id",
                table: "portal_items",
                column: "published_file_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portal_items_published_section",
                table: "portal_items",
                sql: "`published_section` IN ('presentation', 'model', 'drawing')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portal_items_published_whole",
                table: "portal_items",
                sql: "(`published_file_id` IS NULL AND `published_title` IS NULL AND `published_section` IS NULL AND `published_sort_order` IS NULL) OR (`published_file_id` IS NOT NULL AND `published_title` IS NOT NULL AND `published_section` IS NOT NULL AND `published_sort_order` IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portal_items_removed_is_published",
                table: "portal_items",
                sql: "`removed_at` IS NULL OR `published_file_id` IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_portal_settings_hero_file_id",
                table: "portal_settings",
                column: "hero_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_settings_published_by_id",
                table: "portal_settings",
                column: "published_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_settings_published_hero_file_id",
                table: "portal_settings",
                column: "published_hero_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_portal_items_portal_files_published_file_id",
                table: "portal_items",
                column: "published_file_id",
                principalTable: "portal_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_portal_items_portal_files_published_file_id",
                table: "portal_items");

            migrationBuilder.DropTable(
                name: "portal_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portal_uploads_hero_or_section",
                table: "portal_uploads");

            migrationBuilder.DropIndex(
                name: "ix_portal_items_project_id_published_section_published_sort_ord",
                table: "portal_items");

            migrationBuilder.DropIndex(
                name: "ix_portal_items_published_file_id",
                table: "portal_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portal_items_published_section",
                table: "portal_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portal_items_published_whole",
                table: "portal_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portal_items_removed_is_published",
                table: "portal_items");

            migrationBuilder.DropColumn(
                name: "is_hero",
                table: "portal_uploads");

            migrationBuilder.DropColumn(
                name: "published_file_id",
                table: "portal_items");

            migrationBuilder.DropColumn(
                name: "published_section",
                table: "portal_items");

            migrationBuilder.DropColumn(
                name: "published_sort_order",
                table: "portal_items");

            migrationBuilder.DropColumn(
                name: "published_title",
                table: "portal_items");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "portal_items");

            // hero uploads have no section, which the old column can't hold (and an empty one breaks its check):
            // they go, with the hero itself
            migrationBuilder.Sql("DELETE FROM `portal_uploads` WHERE `section` IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "section",
                table: "portal_uploads",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
