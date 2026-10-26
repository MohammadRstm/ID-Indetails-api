using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionSiteStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "material:create", "employee" });

            migrationBuilder.CreateTable(
                name: "project_staff",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    assigned_by_id = table.Column<long>(type: "bigint", nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_staff_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_staff_users_assigned_by_id",
                        column: x => x.assigned_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_staff_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "site_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_logs_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "site_log:create", "Write the daily logs of the sites one oversees", "execution" },
                    { "site_log:view_all", "Read every site's daily logs", "execution" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "site_log:create", "admin" },
                    { "site_log:view_all", "admin" },
                    { "site_log:create", "employee" },
                    { "site_log:create", "super_admin" },
                    { "site_log:view_all", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_staff_assigned_by_id",
                table: "project_staff",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_staff_project_id_user_id",
                table: "project_staff",
                columns: new[] { "project_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_staff_user_id_removed_at",
                table: "project_staff",
                columns: new[] { "user_id", "removed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_site_logs_project_id_created_at",
                table: "site_logs",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_site_logs_user_id_created_at",
                table: "site_logs",
                columns: new[] { "user_id", "created_at" });

            // Sites already running are overseen by the Execution employees who recorded their materials or labour, from
            // when they first did; admins adjust the teams afterwards.
            migrationBuilder.Sql(
                "INSERT INTO `project_staff` (`project_id`, `user_id`, `assigned_at`, `assigned_by_id`, `removed_at`) " +
                "SELECT r.`project_id`, r.`user_id`, MIN(r.`at`), NULL, NULL FROM (" +
                "SELECT `project_id`, `added_by_id` AS `user_id`, `added_at` AS `at` FROM `project_materials` WHERE `deleted_at` IS NULL " +
                "UNION ALL SELECT `project_id`, `submitted_by_id`, `submitted_at` FROM `labour_weeks`) r " +
                "JOIN `users` u ON u.`id` = r.`user_id` AND u.`role_code` = 'employee' AND u.`domain_code` = 'execution' " +
                "JOIN `projects` p ON p.`id` = r.`project_id` AND p.`domain_code` = 'execution' " +
                "GROUP BY r.`project_id`, r.`user_id`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `user_permission_overrides` WHERE `permission_code` IN ('site_log:create', 'site_log:view_all');");

            migrationBuilder.DropTable(
                name: "project_staff");

            migrationBuilder.DropTable(
                name: "site_logs");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "site_log:create", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "site_log:view_all", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "site_log:create", "employee" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "site_log:create", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "site_log:view_all", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "site_log:create");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "site_log:view_all");

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "material:create", "employee" });
        }
    }
}
