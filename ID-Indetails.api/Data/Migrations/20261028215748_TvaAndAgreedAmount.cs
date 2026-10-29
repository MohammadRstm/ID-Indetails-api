using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TvaAndAgreedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tva_rate",
                table: "projects",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "previous_tva_rate",
                table: "project_payment_changes",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tva_rate",
                table: "project_payment_changes",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tva_usd",
                table: "payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_tva_rate_range",
                table: "projects",
                sql: "`tva_rate` >= 0 AND `tva_rate` <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_tva_within_amount",
                table: "payments",
                sql: "`tva_usd` >= 0 AND `tva_usd` <= `amount_usd`");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_tva_rate_range",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_tva_within_amount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "tva_rate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "previous_tva_rate",
                table: "project_payment_changes");

            migrationBuilder.DropColumn(
                name: "tva_rate",
                table: "project_payment_changes");

            migrationBuilder.DropColumn(
                name: "tva_usd",
                table: "payments");
        }
    }
}
