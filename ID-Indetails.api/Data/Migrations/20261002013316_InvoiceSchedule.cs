using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "scheduled_by_id",
                table: "invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "send_on",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_to",
                table: "invoices",
                type: "varchar(254)",
                maxLength: 254,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_scheduled_by_id",
                table: "invoices",
                column: "scheduled_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_send_on",
                table: "invoices",
                column: "send_on");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_scheduled_is_draft",
                table: "invoices",
                sql: "`send_on` IS NULL OR `issued_at` IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_users_scheduled_by_id",
                table: "invoices",
                column: "scheduled_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoices_users_scheduled_by_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_invoices_scheduled_by_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_invoices_send_on",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_scheduled_is_draft",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "scheduled_by_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "send_on",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "send_to",
                table: "invoices");
        }
    }
}
