using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "company_settings",
                type: "varchar(254)",
                maxLength: 254,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "website_favicon_file_id",
                table: "company_settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "website_locations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    address = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phone = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_locations", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_projects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    year = table.Column<int>(type: "int", nullable: true),
                    client = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    description = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    thumbnail_file_id = table.Column<long>(type: "bigint", nullable: true),
                    hero_file_id = table.Column<long>(type: "bigint", nullable: true),
                    hero_alt = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hero_credit_photographer = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hero_credit_source = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hero_credit_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<long>(type: "bigint", nullable: true),
                    published = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    conveyor_order = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_projects", x => x.id);
                    table.CheckConstraint("ck_website_projects_conveyor", "`conveyor_order` IS NULL OR (`published` AND `conveyor_order` BETWEEN 1 AND 20)");
                    table.CheckConstraint("ck_website_projects_follows_link", "`project_id` IS NOT NULL OR (`status` IS NOT NULL AND `year` IS NOT NULL)");
                    table.CheckConstraint("ck_website_projects_position", "(`latitude` IS NULL) = (`longitude` IS NULL)");
                    table.CheckConstraint("ck_website_projects_status", "`status` IN ('in_progress', 'completed')");
                    table.CheckConstraint("ck_website_projects_type", "`type` IN ('villa', 'residential', 'hotel')");
                    table.ForeignKey(
                        name: "fk_website_projects_files_hero_file_id",
                        column: x => x.hero_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_projects_files_thumbnail_file_id",
                        column: x => x.thumbnail_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_projects_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_projects_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_projects_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_services",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_services", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_social_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    label = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_social_links", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_project_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    website_project_id = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<long>(type: "bigint", nullable: false),
                    alt = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    credit_photographer = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    credit_source = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    credit_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_project_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_website_project_images_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_project_images_website_projects_website_project_id",
                        column: x => x.website_project_id,
                        principalTable: "website_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "website:manage", "Edit the company website: projects, services, locations, contact details and favicon", null },
                    { "website:view", "View the company website's content", null }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "website:manage", "super_admin" },
                    { "website:view", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_settings_website_favicon_file_id",
                table: "company_settings",
                column: "website_favicon_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_project_images_file_id",
                table: "website_project_images",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_website_project_images_website_project_id_position",
                table: "website_project_images",
                columns: new[] { "website_project_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_conveyor_order",
                table: "website_projects",
                column: "conveyor_order",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_created_by_id",
                table: "website_projects",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_hero_file_id",
                table: "website_projects",
                column: "hero_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_project_id",
                table: "website_projects",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_slug",
                table: "website_projects",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_thumbnail_file_id",
                table: "website_projects",
                column: "thumbnail_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_projects_updated_by_id",
                table: "website_projects",
                column: "updated_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_company_settings_files_website_favicon_file_id",
                table: "company_settings",
                column: "website_favicon_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_company_settings_files_website_favicon_file_id",
                table: "company_settings");

            migrationBuilder.DropTable(
                name: "website_locations");

            migrationBuilder.DropTable(
                name: "website_project_images");

            migrationBuilder.DropTable(
                name: "website_services");

            migrationBuilder.DropTable(
                name: "website_social_links");

            migrationBuilder.DropTable(
                name: "website_projects");

            migrationBuilder.DropIndex(
                name: "ix_company_settings_website_favicon_file_id",
                table: "company_settings");

            // grants and revokes made to people go before the permissions themselves
            migrationBuilder.Sql("DELETE FROM `user_permission_overrides` WHERE `permission_code` IN ('website:view', 'website:manage');");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "website:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "website:view", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "website:view");

            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "website_favicon_file_id",
                table: "company_settings");
        }
    }
}
