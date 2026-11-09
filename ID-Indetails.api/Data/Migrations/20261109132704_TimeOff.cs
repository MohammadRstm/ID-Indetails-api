using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimeOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "time_off_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    first_day = table.Column<DateOnly>(type: "date", nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_paid = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    decided_by_id = table.Column<long>(type: "bigint", nullable: true),
                    decision_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ended_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    ended_by_id = table.Column<long>(type: "bigint", nullable: true),
                    end_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_off_requests", x => x.id);
                    table.CheckConstraint("ck_time_off_requests_days", "`last_day` >= `first_day`");
                    table.CheckConstraint("ck_time_off_requests_ended_on", "(`status` IN ('rejected', 'ended')) = (`ended_on` IS NOT NULL) AND (`ended_on` IS NULL OR `ended_on` <= `last_day`)");
                    table.CheckConstraint("ck_time_off_requests_is_paid", "(`status` IN ('approved', 'rejected', 'ended')) = (`is_paid` IS NOT NULL)");
                    table.CheckConstraint("ck_time_off_requests_status", "`status` IN ('pending', 'approved', 'declined', 'withdrawn', 'expired', 'cancelled', 'rejected', 'ended')");
                    table.ForeignKey(
                        name: "fk_time_off_requests_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_time_off_requests_users_ended_by_id",
                        column: x => x.ended_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_time_off_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "time_off:decide", "Approve time off, with pay or without, decline it, or take approved time off back", null },
                    { "time_off:request", "Ask for time off", "design" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "time_off:request", "employee" },
                    { "time_off:decide", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_decided_by_id",
                table: "time_off_requests",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_ended_by_id",
                table: "time_off_requests",
                column: "ended_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_status_first_day",
                table: "time_off_requests",
                columns: new[] { "status", "first_day" });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_user_id_status_first_day",
                table: "time_off_requests",
                columns: new[] { "user_id", "status", "first_day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "time_off_requests");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "time_off:request", "employee" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "time_off:decide", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "time_off:decide");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "time_off:request");
        }
    }
}
