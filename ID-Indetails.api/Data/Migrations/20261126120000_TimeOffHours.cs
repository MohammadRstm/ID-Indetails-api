using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Hours someone logs on a day of their own time off wait for a super admin, as a blocked change of kind
    /// <c>work_entry_time_off</c>. Approved as overtime, the entry is marked <c>time_off_overtime</c>: all its minutes are
    /// overtime. Every existing entry is not.
    /// </summary>
    public partial class TimeOffHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "time_off_overtime",
                table: "work_entries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department', 'design_vo', 'pay_change', 'work_entry_time_off')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // hours held for a day off can't be kept without their kind
            migrationBuilder.Sql("DELETE FROM `blocked_changes` WHERE `kind` = 'work_entry_time_off';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department', 'design_vo', 'pay_change')");

            migrationBuilder.DropColumn(
                name: "time_off_overtime",
                table: "work_entries");
        }
    }
}
