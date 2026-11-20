using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// A project's optional price in CFA francs, typed in by hand as a reference for whoever sees the money. It is in no
    /// calculation, so existing projects simply have none.
    /// </summary>
    public partial class ProjectCfaPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cfa_price",
                table: "projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cfa_price",
                table: "projects");
        }
    }
}
