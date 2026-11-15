using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Pay changes may be dated on or before the latest one: a change dated D replaces every active row from D onward,
    /// which is kept, marked superseded. Every existing row stays active, so no data changes. The unique index on
    /// (user_id, effective_from) becomes one on (user_id, active_effective_from), a generated column that is NULL once a
    /// row is superseded. The new index is made before the old one goes, so user_id's foreign key always has one.
    /// </summary>
    public partial class SupersededPayEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "superseded_at",
                table: "user_pay_history",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "superseded_by_id",
                table: "user_pay_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "active_effective_from",
                table: "user_pay_history",
                type: "date",
                nullable: true,
                computedColumnSql: "IF(`superseded_at` IS NULL, `effective_from`, NULL)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_pay_history_superseded_by_id",
                table: "user_pay_history",
                column: "superseded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_pay_history_user_id_active_effective_from",
                table: "user_pay_history",
                columns: new[] { "user_id", "active_effective_from" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_user_pay_history_user_id_effective_from",
                table: "user_pay_history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_pay_history_superseded",
                table: "user_pay_history",
                sql: "(`superseded_at` IS NULL) = (`superseded_by_id` IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_user_pay_history_users_superseded_by_id",
                table: "user_pay_history",
                column: "superseded_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // only active rows fit the old index: superseded ones are dropped, their record with them
            migrationBuilder.Sql("DELETE FROM `user_pay_history` WHERE `superseded_at` IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_user_pay_history_users_superseded_by_id",
                table: "user_pay_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_pay_history_superseded",
                table: "user_pay_history");

            migrationBuilder.CreateIndex(
                name: "ix_user_pay_history_user_id_effective_from",
                table: "user_pay_history",
                columns: new[] { "user_id", "effective_from" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_user_pay_history_user_id_active_effective_from",
                table: "user_pay_history");

            migrationBuilder.DropIndex(
                name: "ix_user_pay_history_superseded_by_id",
                table: "user_pay_history");

            migrationBuilder.DropColumn(
                name: "active_effective_from",
                table: "user_pay_history");

            migrationBuilder.DropColumn(
                name: "superseded_by_id",
                table: "user_pay_history");

            migrationBuilder.DropColumn(
                name: "superseded_at",
                table: "user_pay_history");
        }
    }
}
