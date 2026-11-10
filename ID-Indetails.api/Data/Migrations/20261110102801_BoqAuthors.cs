using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoqAuthors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "boqs",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submitted_lines",
                table: "boqs",
                type: "mediumtext",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "boq:create",
                columns: new[] { "description", "domain_code" },
                values: new object[] { "Write internal draft BOQs for Execution's active sites and submit them, from either business", null });

            // the internal drafts written before submitting existed were all seen by pricing: they count as submitted, with
            // no copy of their lines to compare against
            migrationBuilder.Sql("UPDATE `boqs` SET `submitted_at` = `created_at` WHERE `kind` = 'internal';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_boqs_submitted_internal",
                table: "boqs",
                sql: "`submitted_at` IS NULL OR `kind` = 'internal'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_boqs_submitted_lines",
                table: "boqs",
                sql: "`submitted_lines` IS NULL OR `submitted_at` IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_boqs_submitted_internal",
                table: "boqs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_boqs_submitted_lines",
                table: "boqs");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "boqs");

            migrationBuilder.DropColumn(
                name: "submitted_lines",
                table: "boqs");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "boq:create",
                columns: new[] { "description", "domain_code" },
                values: new object[] { "Write internal draft BOQs of a site", "execution" });
        }
    }
}
