using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing billing settings get the defaults (off; the day after, every 7 days, at most 3), which the
            // check below needs.
            migrationBuilder.AddColumn<bool>(
                name: "is_reminder",
                table: "invoice_deliveries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reminder_every_days",
                table: "billing_settings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "reminder_first_after_days",
                table: "billing_settings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "reminder_max",
                table: "billing_settings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "reminders_enabled",
                table: "billing_settings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_billing_settings_reminders",
                table: "billing_settings",
                sql: "`reminder_first_after_days` BETWEEN 1 AND 60 AND `reminder_every_days` BETWEEN 1 AND 60 AND `reminder_max` BETWEEN 1 AND 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_billing_settings_reminders",
                table: "billing_settings");

            migrationBuilder.DropColumn(
                name: "is_reminder",
                table: "invoice_deliveries");

            migrationBuilder.DropColumn(
                name: "reminder_every_days",
                table: "billing_settings");

            migrationBuilder.DropColumn(
                name: "reminder_first_after_days",
                table: "billing_settings");

            migrationBuilder.DropColumn(
                name: "reminder_max",
                table: "billing_settings");

            migrationBuilder.DropColumn(
                name: "reminders_enabled",
                table: "billing_settings");
        }
    }
}
