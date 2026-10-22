using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteProjectFreeType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_website_projects_type",
                table: "website_projects");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "website_projects",
                type: "varchar(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // the fixed keys become the types as shown
            migrationBuilder.Sql("UPDATE `website_projects` SET `type` = CONCAT(UPPER(LEFT(`type`, 1)), SUBSTRING(`type`, 2));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // back to the three fixed keys: any other type becomes a villa
            migrationBuilder.Sql(
                "UPDATE `website_projects` SET `type` = CASE LOWER(`type`) WHEN 'residential' THEN 'residential' WHEN 'hotel' THEN 'hotel' ELSE 'villa' END;");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "website_projects",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(60)",
                oldMaxLength: 60)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_website_projects_type",
                table: "website_projects",
                sql: "`type` IN ('villa', 'residential', 'hotel')");
        }
    }
}
