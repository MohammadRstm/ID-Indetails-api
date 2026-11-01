using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OvertimeThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "overtime_threshold_hours",
                table: "working_time_policies",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.UpdateData(
                table: "working_time_policies",
                keyColumn: "id",
                keyValue: 1L,
                column: "overtime_threshold_hours",
                value: (short)0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_working_time_policies_overtime_threshold",
                table: "working_time_policies",
                sql: "`overtime_threshold_hours` BETWEEN 0 AND 744");

            // From 1 November 2026 a salary's overtime is paid only past 184 hours a month: the week in force then carries
            // on with the threshold, and any working time already set after it gets it too.
            migrationBuilder.Sql(@"
INSERT INTO `working_time_policies`
    (`effective_from`, `work_start_minute`, `work_end_minute`, `work_days`, `break_minutes`, `overtime_multiplier`, `overtime_threshold_hours`, `created_at`, `created_by_id`)
SELECT '2026-11-01', p.`work_start_minute`, p.`work_end_minute`, p.`work_days`, p.`break_minutes`, p.`overtime_multiplier`, 184, UTC_TIMESTAMP(3), NULL
FROM `working_time_policies` p
WHERE p.`effective_from` = (SELECT MAX(`effective_from`) FROM `working_time_policies` WHERE `effective_from` <= '2026-11-01')
  AND NOT EXISTS (SELECT 1 FROM `working_time_policies` WHERE `effective_from` = '2026-11-01');");
            migrationBuilder.Sql("UPDATE `working_time_policies` SET `overtime_threshold_hours` = 184 WHERE `effective_from` >= '2026-11-01';");

            // From 1 December 2026 the break is 0, so the whole 8-hour day is logged (decision 25). The rules were already
            // there; the working time that sets it was not, so it is added unless someone set one already.
            migrationBuilder.Sql(@"
INSERT INTO `working_time_policies`
    (`effective_from`, `work_start_minute`, `work_end_minute`, `work_days`, `break_minutes`, `overtime_multiplier`, `overtime_threshold_hours`, `created_at`, `created_by_id`)
SELECT '2026-12-01', p.`work_start_minute`, p.`work_end_minute`, p.`work_days`, 0, p.`overtime_multiplier`, 184, UTC_TIMESTAMP(3), NULL
FROM `working_time_policies` p
WHERE p.`effective_from` = (SELECT MAX(`effective_from`) FROM `working_time_policies` WHERE `effective_from` <= '2026-12-01')
  AND NOT EXISTS (SELECT 1 FROM `working_time_policies` WHERE `effective_from` >= '2026-12-01');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // only the rows this migration added: nobody created them
            migrationBuilder.Sql("DELETE FROM `working_time_policies` WHERE `created_by_id` IS NULL AND `effective_from` IN ('2026-11-01', '2026-12-01');");

            migrationBuilder.DropCheckConstraint(
                name: "ck_working_time_policies_overtime_threshold",
                table: "working_time_policies");

            migrationBuilder.DropColumn(
                name: "overtime_threshold_hours",
                table: "working_time_policies");
        }
    }
}
