using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmploymentPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employment_periods",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_periods", x => x.id);
                    table.CheckConstraint("ck_employment_periods_ends_after_start", "`ended_on` IS NULL OR `ended_on` >= `started_on`");
                    table.ForeignKey(
                        name: "fk_employment_periods_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employment_periods_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_employment_periods_created_by_id",
                table: "employment_periods",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_periods_user_id_started_on",
                table: "employment_periods",
                columns: new[] { "user_id", "started_on" },
                unique: true);

            // One period for everyone who has worked here: from when they joined to the day their account was
            // deactivated. Dates come from UTC timestamps, so they can be a day off near midnight; nothing is in
            // production yet. Someone deactivated with no known date gets none and stays out of salaries, as before.
            migrationBuilder.Sql("""
                INSERT INTO `employment_periods` (`user_id`, `started_on`, `ended_on`, `created_at`)
                SELECT `id`,
                       COALESCE(`joined_on`, DATE(`created_at`)),
                       CASE WHEN `status` = 'deactivated'
                            THEN GREATEST(DATE(`deactivated_at`), COALESCE(`joined_on`, DATE(`created_at`)))
                       END,
                       UTC_TIMESTAMP(3)
                FROM `users`
                WHERE `status` = 'active' OR (`status` = 'deactivated' AND `deactivated_at` IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employment_periods");
        }
    }
}
