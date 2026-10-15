using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "client_portal:manage", "admin" });

            migrationBuilder.AddColumn<bool>(
                name: "for_client",
                table: "portal_items",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // the files already in the client portal were uploaded for the client
            migrationBuilder.Sql("UPDATE `portal_items` SET `for_client` = 1;");

            migrationBuilder.CreateTable(
                name: "project_file_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    edited_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    fixed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    fixed_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_file_comments", x => x.id);
                    table.CheckConstraint("ck_project_file_comments_fixed", "(`fixed_at` IS NULL) = (`fixed_by_id` IS NULL)");
                    table.ForeignKey(
                        name: "fk_project_file_comments_portal_items_item_id",
                        column: x => x.item_id,
                        principalTable: "portal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_file_comments_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_file_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_file_comments_users_fixed_by_id",
                        column: x => x.fixed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "client_portal:manage",
                column: "description",
                value: "Choose which project files a client sees, and publish the client portal");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "project_files:manage", "Manage anyone's project files and start comments on them", "design" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "project_files:manage", "admin" },
                    { "project_files:manage", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_file_comments_author_id",
                table: "project_file_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_file_comments_fixed_by_id",
                table: "project_file_comments",
                column: "fixed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_file_comments_item_id_id",
                table: "project_file_comments",
                columns: new[] { "item_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_file_comments_project_id",
                table: "project_file_comments",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_file_comments");

            // grants and revokes made to people go before the permission itself
            migrationBuilder.Sql("DELETE FROM `user_permission_overrides` WHERE `permission_code` = 'project_files:manage';");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project_files:manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "project_files:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "project_files:manage");

            migrationBuilder.DropColumn(
                name: "for_client",
                table: "portal_items");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "client_portal:manage",
                column: "description",
                value: "Upload and manage the files for a project's client portal");

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "client_portal:manage", "admin" });
        }
    }
}
