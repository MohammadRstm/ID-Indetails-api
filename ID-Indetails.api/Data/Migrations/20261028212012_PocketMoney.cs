using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PocketMoney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pocket_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    suggested_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requested_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    requested_by_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    decided_by_id = table.Column<long>(type: "bigint", nullable: true),
                    decision_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pocket_requests", x => x.id);
                    table.CheckConstraint("ck_pocket_requests_approved_amount", "(`status` = 'approved' AND `approved_amount` > 0) OR (`status` <> 'approved' AND `approved_amount` IS NULL)");
                    table.CheckConstraint("ck_pocket_requests_status", "`status` IN ('pending', 'approved', 'denied')");
                    table.CheckConstraint("ck_pocket_requests_suggested_non_negative", "`suggested_amount` >= 0");
                    table.ForeignKey(
                        name: "fk_pocket_requests_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pocket_requests_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pocket_requests_users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pocket_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    request_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pocket_entries", x => x.id);
                    table.CheckConstraint("ck_pocket_entries_amount_sign", "(`kind` = 'contribution' AND `amount` > 0) OR (`kind` = 'payout' AND `amount` < 0)");
                    table.CheckConstraint("ck_pocket_entries_kind", "`kind` IN ('contribution', 'payout')");
                    table.ForeignKey(
                        name: "fk_pocket_entries_pocket_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "pocket_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pocket_entries_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pocket_entries_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_entries_created_by_id",
                table: "pocket_entries",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_entries_kind_created_at",
                table: "pocket_entries",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pocket_entries_project_id",
                table: "pocket_entries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_entries_request_id",
                table: "pocket_entries",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pocket_requests_decided_by_id",
                table: "pocket_requests",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_requests_project_id",
                table: "pocket_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_requests_requested_by_id",
                table: "pocket_requests",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_pocket_requests_status_requested_at",
                table: "pocket_requests",
                columns: new[] { "status", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pocket_entries");

            migrationBuilder.DropTable(
                name: "pocket_requests");
        }
    }
}
