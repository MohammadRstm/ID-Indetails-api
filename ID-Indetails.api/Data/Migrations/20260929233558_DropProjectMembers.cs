using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropProjectMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "disciplines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "disciplines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disciplines", x => x.id);
                    table.ForeignKey(
                        name: "fk_disciplines_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    discipline_id = table.Column<long>(type: "bigint", nullable: true),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_project_members_disciplines_discipline_id",
                        column: x => x.discipline_id,
                        principalTable: "disciplines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_disciplines_domain_code_name",
                table: "disciplines",
                columns: new[] { "domain_code", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_members_added_by_id",
                table: "project_members",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_discipline_id",
                table: "project_members",
                column: "discipline_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                table: "project_members",
                column: "user_id");
        }
    }
}
