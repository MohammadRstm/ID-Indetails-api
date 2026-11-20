using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SalaryAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salary_advances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    asked_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    decided_by_id = table.Column<long>(type: "bigint", nullable: true),
                    decision_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    given_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    cancelled_by_id = table.Column<long>(type: "bigint", nullable: true),
                    settled_kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settled_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    settled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    settled_by_id = table.Column<long>(type: "bigint", nullable: true),
                    settlement_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_advances", x => x.id);
                    table.CheckConstraint("ck_salary_advances_amount_positive", "`amount` > 0 AND (`asked_amount` IS NULL OR `asked_amount` > 0)");
                    table.CheckConstraint("ck_salary_advances_given_on", "(`status` IN ('approved', 'cancelled')) = (`given_on` IS NOT NULL)");
                    table.CheckConstraint("ck_salary_advances_settled", "(`settled_kind` IS NULL) = (`settled_on` IS NULL) AND (`settled_kind` IS NULL) = (`settled_amount` IS NULL) AND (`settled_kind` IS NULL OR `status` = 'approved') AND (`settled_amount` IS NULL OR `settled_amount` > 0)");
                    table.CheckConstraint("ck_salary_advances_settled_kind", "`settled_kind` IN ('cash', 'written_off')");
                    table.CheckConstraint("ck_salary_advances_status", "`status` IN ('pending', 'approved', 'declined', 'withdrawn', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_salary_advances_users_cancelled_by_id",
                        column: x => x.cancelled_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salary_advances_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salary_advances_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salary_advances_users_settled_by_id",
                        column: x => x.settled_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salary_advances_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "salary_advance_repayments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    advance_id = table.Column<long>(type: "bigint", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_advance_repayments", x => x.id);
                    table.CheckConstraint("ck_salary_advance_repayments_amount_positive", "`amount` > 0");
                    table.CheckConstraint("ck_salary_advance_repayments_month", "DAY(`month`) = 1");
                    table.ForeignKey(
                        name: "fk_salary_advance_repayments_salary_advances_advance_id",
                        column: x => x.advance_id,
                        principalTable: "salary_advances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[] { "salary_advance:manage", "Give salary advances, decide people's requests for one, and settle what is left of them", null });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[] { "salary_advance:manage", "super_admin" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_advance_repayments_advance_id_month",
                table: "salary_advance_repayments",
                columns: new[] { "advance_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_cancelled_by_id",
                table: "salary_advances",
                column: "cancelled_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_created_by_id",
                table: "salary_advances",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_decided_by_id",
                table: "salary_advances",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_settled_by_id",
                table: "salary_advances",
                column: "settled_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_status_created_at",
                table: "salary_advances",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_advances_user_id_status",
                table: "salary_advances",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salary_advance_repayments");

            migrationBuilder.DropTable(
                name: "salary_advances");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "salary_advance:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "salary_advance:manage");
        }
    }
}
