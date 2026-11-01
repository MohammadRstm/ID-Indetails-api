using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OvertimeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_pay_history_rate_non_negative",
                table: "user_pay_history");

            migrationBuilder.RenameColumn(
                name: "hourly_cost_rate",
                table: "user_pay_history",
                newName: "overtime_rate");

            // Every pay record gets its overtime rate: hourly pay its own, a salary the suggested salary ÷ 184, to the cent.
            migrationBuilder.Sql(
                "UPDATE `user_pay_history` SET `overtime_rate` = CASE WHEN `basis` = 'hourly' THEN `amount` ELSE ROUND(`amount` / 184, 2) END;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_pay_history_rate_non_negative",
                table: "user_pay_history",
                sql: "`overtime_rate` >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_pay_history_rate_non_negative",
                table: "user_pay_history");

            // As the column was before: hourly pay's rate, 0 on a salary.
            migrationBuilder.Sql("UPDATE `user_pay_history` SET `overtime_rate` = CASE WHEN `basis` = 'hourly' THEN `amount` ELSE 0 END;");

            migrationBuilder.RenameColumn(
                name: "overtime_rate",
                table: "user_pay_history",
                newName: "hourly_cost_rate");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_pay_history_rate_non_negative",
                table: "user_pay_history",
                sql: "`hourly_cost_rate` >= 0");
        }
    }
}
