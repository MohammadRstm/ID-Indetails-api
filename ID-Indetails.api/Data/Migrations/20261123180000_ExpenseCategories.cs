using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Expense categories each business manages (23 November 2026), seeded with the ones the code had, so every expense
    /// already recorded keeps its category: Design's project expenses and Execution's site expenses. Site expenses lose
    /// the check that fixed their five categories; the API checks them against the table instead. No expense changes.
    /// </summary>
    public partial class ExpenseCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_site_expenses_category",
                table: "site_expenses");

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_categories_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_domain_code_code",
                table: "expense_categories",
                columns: new[] { "domain_code", "code" },
                unique: true);

            // the categories the code had, in its order
            migrationBuilder.Sql(
                """
                INSERT INTO `expense_categories` (`domain_code`, `code`, `name`, `sort_order`, `created_at`) VALUES
                    ('design', 'assets', 'Assets', 0, UTC_TIMESTAMP(3)),
                    ('design', 'services', 'Services', 1, UTC_TIMESTAMP(3)),
                    ('design', 'contractor', 'Contractor', 2, UTC_TIMESTAMP(3)),
                    ('design', 'software', 'Software', 3, UTC_TIMESTAMP(3)),
                    ('design', 'travel', 'Travel', 4, UTC_TIMESTAMP(3)),
                    ('design', 'hardware', 'Hardware', 5, UTC_TIMESTAMP(3)),
                    ('design', 'other', 'Other', 6, UTC_TIMESTAMP(3)),
                    ('execution', 'materials', 'Materials', 0, UTC_TIMESTAMP(3)),
                    ('execution', 'contractor', 'Contractor', 1, UTC_TIMESTAMP(3)),
                    ('execution', 'equipment', 'Equipment', 2, UTC_TIMESTAMP(3)),
                    ('execution', 'fees', 'Fees', 3, UTC_TIMESTAMP(3)),
                    ('execution', 'other', 'Other', 4, UTC_TIMESTAMP(3));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.AddCheckConstraint(
                name: "ck_site_expenses_category",
                table: "site_expenses",
                sql: "`category` IN ('materials', 'contractor', 'equipment', 'fees', 'other')");
        }
    }
}
