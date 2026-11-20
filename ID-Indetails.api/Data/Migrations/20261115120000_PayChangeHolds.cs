using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// A pay change that would take a running client project's profit to zero or below is held as a blocked change of
    /// kind <c>pay_change</c>: only its kind check changes. The hardest-hit project is its <c>project_id</c>; the person,
    /// the request, every project it reaches and the pay version it was held at are in its payload.
    /// </summary>
    public partial class PayChangeHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department', 'design_vo', 'pay_change')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a held pay change can't be kept without its kind
            migrationBuilder.Sql("DELETE FROM `blocked_changes` WHERE `kind` = 'pay_change';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_blocked_changes_kind",
                table: "blocked_changes",
                sql: "`kind` IN ('expense', 'material_add', 'material_update', 'labour_week', 'work_entry_create', 'work_entry_update', 'payment', 'pricing', 'boq_agree', 'boq_withdraw', 'work_entry_department', 'design_vo')");
        }
    }
}
