using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserPermissionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    permission_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effect = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    granted_by_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permission_overrides", x => new { x.user_id, x.permission_code });
                    table.CheckConstraint("ck_user_permission_overrides_effect", "`effect` IN ('grant', 'revoke')");
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_permissions_permission_code",
                        column: x => x.permission_code,
                        principalTable: "permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_users_user_id",
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
                    { "employee:invite_super_admin", "Invite super admins", null },
                    { "permission:manage", "Grant and revoke other people's permissions", null }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "employee:invite_super_admin", "super_admin" },
                    { "permission:manage", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_granted_by_id",
                table: "user_permission_overrides",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_permission_code",
                table: "user_permission_overrides",
                column: "permission_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_permission_overrides");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "employee:invite_super_admin", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "permission:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "employee:invite_super_admin");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "permission:manage");
        }
    }
}
