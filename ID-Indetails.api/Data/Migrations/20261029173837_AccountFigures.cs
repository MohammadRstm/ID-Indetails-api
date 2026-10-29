using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountFigures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "account_label_id",
                table: "payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supervision_withdrawals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    amount_usd = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    taken_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supervision_withdrawals", x => x.id);
                    table.CheckConstraint("ck_supervision_withdrawals_amount_positive", "`amount_usd` > 0");
                    table.ForeignKey(
                        name: "fk_supervision_withdrawals_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supervision_withdrawals_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_payments_account_label_id",
                table: "payments",
                column: "account_label_id");

            migrationBuilder.CreateIndex(
                name: "ix_supervision_withdrawals_created_by_id",
                table: "supervision_withdrawals",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_supervision_withdrawals_project_id",
                table: "supervision_withdrawals",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_account_labels_account_label_id",
                table: "payments",
                column: "account_label_id",
                principalTable: "account_labels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_account_labels_account_label_id",
                table: "payments");

            migrationBuilder.DropTable(
                name: "supervision_withdrawals");

            migrationBuilder.DropIndex(
                name: "ix_payments_account_label_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "account_label_id",
                table: "payments");
        }
    }
}
