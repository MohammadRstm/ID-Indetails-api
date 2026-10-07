using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtonUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proton_budget_top_ups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_usd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    added_by_id = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proton_budget_top_ups", x => x.id);
                    table.CheckConstraint("ck_proton_budget_top_ups_amount_positive", "`amount_usd` > 0");
                    table.ForeignKey(
                        name: "fk_proton_budget_top_ups_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "proton_usage",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    conversation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurred_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    model = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prompt_version = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    round_trips = table.Column<int>(type: "int", nullable: false),
                    tool_calls = table.Column<int>(type: "int", nullable: false),
                    input_tokens = table.Column<int>(type: "int", nullable: false),
                    output_tokens = table.Column<int>(type: "int", nullable: false),
                    cache_read_tokens = table.Column<int>(type: "int", nullable: false),
                    cache_write_tokens = table.Column<int>(type: "int", nullable: false),
                    cost_usd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    question = table.Column<string>(type: "text", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answer = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tools = table.Column<string>(type: "json", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proton_usage", x => x.id);
                    table.CheckConstraint("ck_proton_usage_outcome", "`outcome` IN ('answered', 'refused', 'limited', 'failed')");
                    table.ForeignKey(
                        name: "fk_proton_usage_users_user_id",
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
                    { "proton_budget:manage", "See Proton's costs and usage, and top up its monthly budget", null },
                    { "proton:use", "Ask Proton, the read-only company assistant", null }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "proton:use", "admin" },
                    { "proton_budget:manage", "super_admin" },
                    { "proton:use", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_proton_budget_top_ups_added_by_id",
                table: "proton_budget_top_ups",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_proton_budget_top_ups_month",
                table: "proton_budget_top_ups",
                column: "month");

            migrationBuilder.CreateIndex(
                name: "ix_proton_usage_occurred_at",
                table: "proton_usage",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_proton_usage_user_id_occurred_at",
                table: "proton_usage",
                columns: new[] { "user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proton_budget_top_ups");

            migrationBuilder.DropTable(
                name: "proton_usage");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "proton:use", "admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "proton_budget:manage", "super_admin" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_code" },
                keyValues: new object[] { "proton:use", "super_admin" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "proton_budget:manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "proton:use");
        }
    }
}
