using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "portal_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(127)", maxLength: 127, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    stored_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_files", x => x.id);
                    table.CheckConstraint("ck_portal_files_original_size_positive", "`original_size_bytes` > 0");
                    table.CheckConstraint("ck_portal_files_stored_size_non_negative", "`stored_size_bytes` >= 0");
                    table.ForeignKey(
                        name: "fk_portal_files_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_files_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_items", x => x.id);
                    table.CheckConstraint("ck_portal_items_section", "`section` IN ('presentation', 'model', 'drawing')");
                    table.ForeignKey(
                        name: "fk_portal_items_portal_files_file_id",
                        column: x => x.file_id,
                        principalTable: "portal_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_items_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_items_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_uploads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    replace_item_id = table.Column<long>(type: "bigint", nullable: true),
                    client_token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    storage_key = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    store_upload_id = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_uploads", x => x.id);
                    table.CheckConstraint("ck_portal_uploads_section", "`section` IN ('presentation', 'model', 'drawing')");
                    table.CheckConstraint("ck_portal_uploads_size_positive", "`size_bytes` > 0");
                    table.CheckConstraint("ck_portal_uploads_status", "`status` IN ('uploading', 'completed', 'cancelled', 'rejected', 'expired')");
                    table.ForeignKey(
                        name: "fk_portal_uploads_portal_items_replace_item_id",
                        column: x => x.replace_item_id,
                        principalTable: "portal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_uploads_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_uploads_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "client_portal:manage", "Upload and manage the files for a project's client portal", "design" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "client_portal:manage", "admin" },
                    { "client_portal:manage", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_portal_files_deleted_at",
                table: "portal_files",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_portal_files_project_id_deleted_at",
                table: "portal_files",
                columns: new[] { "project_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_files_storage_key",
                table: "portal_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_files_uploaded_by_id",
                table: "portal_files",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_items_file_id",
                table: "portal_items",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_items_project_id_section_sort_order",
                table: "portal_items",
                columns: new[] { "project_id", "section", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_items_updated_by_id",
                table: "portal_items",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_uploads_created_by_id_client_token",
                table: "portal_uploads",
                columns: new[] { "created_by_id", "client_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_uploads_project_id",
                table: "portal_uploads",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_uploads_replace_item_id",
                table: "portal_uploads",
                column: "replace_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_uploads_status_last_activity_at",
                table: "portal_uploads",
                columns: new[] { "status", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_uploads_storage_key",
                table: "portal_uploads",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portal_uploads");

            migrationBuilder.DropTable(
                name: "portal_items");

            migrationBuilder.DropTable(
                name: "portal_files");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "client_portal:manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "client_portal:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "client_portal:manage");
        }
    }
}
