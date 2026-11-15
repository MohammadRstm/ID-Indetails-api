using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Everyone is paid a monthly salary: an hourly row becomes a monthly one with its amount unchanged and the overtime
    /// rate a salary is given (amount ÷ 184, to the cent). Monthly rows are left exactly as they are.
    /// </summary>
    public partial class RemovePayBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE `user_pay_history` SET `basis` = 'monthly', `overtime_rate` = ROUND(`amount` / 184, 2) WHERE `basis` = 'hourly';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_pay_history_basis",
                table: "user_pay_history");

            migrationBuilder.DropColumn(
                name: "basis",
                table: "user_pay_history");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the hourly rows can't come back: every row returns as monthly
            migrationBuilder.AddColumn<string>(
                name: "basis",
                table: "user_pay_history",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "monthly")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_pay_history_basis",
                table: "user_pay_history",
                sql: "`basis` IN ('hourly', 'monthly')");
        }
    }
}
