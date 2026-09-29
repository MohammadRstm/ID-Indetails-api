using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserDeactivatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deactivated_at",
                table: "users",
                type: "datetime(3)",
                precision: 3,
                nullable: true);

            // People already deactivated left when their latest deactivation was recorded. Anyone without such an
            // event stays null and is left out of salaries, as before.
            migrationBuilder.Sql(
                """
                UPDATE users u
                SET u.deactivated_at = (
                    SELECT MAX(e.occurred_at)
                    FROM activity_events e
                    WHERE e.kind = 'employee_deactivated'
                      AND e.subject_type = 'user'
                      AND e.subject_id = u.id)
                WHERE u.status = 'deactivated';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deactivated_at",
                table: "users");
        }
    }
}
