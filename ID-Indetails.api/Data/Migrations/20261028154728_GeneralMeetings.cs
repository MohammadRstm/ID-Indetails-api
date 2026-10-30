using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "project_id",
                table: "project_meetings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "domain_code",
                table: "project_meetings",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "meeting_attendees",
                columns: table => new
                {
                    meeting_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_attendees", x => new { x.meeting_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_meeting_attendees_project_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "project_meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_attendees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_meetings_domain_code",
                table: "project_meetings",
                column: "domain_code");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_meetings_project_or_domain",
                table: "project_meetings",
                sql: "`project_id` IS NULL OR `domain_code` IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_attendees_user_id",
                table: "meeting_attendees",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_meetings_domains_domain_code",
                table: "project_meetings",
                column: "domain_code",
                principalTable: "domains",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_meetings_domains_domain_code",
                table: "project_meetings");

            migrationBuilder.DropTable(
                name: "meeting_attendees");

            // a general meeting can't be kept without a project
            migrationBuilder.Sql("DELETE FROM `project_meetings` WHERE `project_id` IS NULL;");

            migrationBuilder.DropIndex(
                name: "ix_project_meetings_domain_code",
                table: "project_meetings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_meetings_project_or_domain",
                table: "project_meetings");

            migrationBuilder.DropColumn(
                name: "domain_code",
                table: "project_meetings");

            migrationBuilder.AlterColumn<long>(
                name: "project_id",
                table: "project_meetings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
