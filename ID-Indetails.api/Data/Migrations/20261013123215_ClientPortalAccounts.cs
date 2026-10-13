using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "portal_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_hash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sessions_valid_from = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    failed_sign_ins = table.Column<int>(type: "int", nullable: false),
                    locked_until = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    activated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_accounts", x => x.id);
                    table.CheckConstraint("ck_portal_accounts_active_has_credentials", "`status` <> 'active' OR (`password_hash` IS NOT NULL AND `first_name` IS NOT NULL AND `last_name` IS NOT NULL)");
                    table.CheckConstraint("ck_portal_accounts_status", "`status` IN ('invited', 'active')");
                    table.ForeignKey(
                        name: "fk_portal_accounts_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    accepted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    invited_by_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_portal_invitations_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_invitations_users_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    used_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_portal_password_reset_tokens_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_project_access",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    granted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    granted_by_id = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    revoked_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_project_access", x => x.id);
                    table.ForeignKey(
                        name: "fk_portal_project_access_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_project_access_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_project_access_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_project_access_users_revoked_by_id",
                        column: x => x.revoked_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "portal_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    replaced_by_id = table.Column<long>(type: "bigint", nullable: true),
                    revoke_reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_portal_refresh_tokens_revoke_reason", "`revoke_reason` IN ('rotated', 'superseded', 'signed_out', 'revoked')");
                    table.ForeignKey(
                        name: "fk_portal_refresh_tokens_portal_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "portal_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portal_refresh_tokens_portal_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "portal_refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "client_access:manage", "Invite clients to a project's client portal and end their access", "design" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "client_access:manage", "admin" },
                    { "client_access:manage", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_portal_accounts_created_by_id",
                table: "portal_accounts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_accounts_email",
                table: "portal_accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_invitations_account_id_project_id",
                table: "portal_invitations",
                columns: new[] { "account_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_portal_invitations_invited_by_id",
                table: "portal_invitations",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_invitations_project_id",
                table: "portal_invitations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_invitations_token_hash",
                table: "portal_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_password_reset_tokens_account_id",
                table: "portal_password_reset_tokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_password_reset_tokens_token_hash",
                table: "portal_password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_project_access_account_id_project_id",
                table: "portal_project_access",
                columns: new[] { "account_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_project_access_granted_by_id",
                table: "portal_project_access",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_project_access_project_id",
                table: "portal_project_access",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_project_access_revoked_by_id",
                table: "portal_project_access",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_refresh_tokens_account_id",
                table: "portal_refresh_tokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_refresh_tokens_replaced_by_id",
                table: "portal_refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_refresh_tokens_session_id",
                table: "portal_refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_refresh_tokens_token_hash",
                table: "portal_refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portal_invitations");

            migrationBuilder.DropTable(
                name: "portal_password_reset_tokens");

            migrationBuilder.DropTable(
                name: "portal_project_access");

            migrationBuilder.DropTable(
                name: "portal_refresh_tokens");

            migrationBuilder.DropTable(
                name: "portal_accounts");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "client_access:manage", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "client_access:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "client_access:manage");
        }
    }
}
